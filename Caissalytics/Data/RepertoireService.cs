using System.Text;
using System.Text.Json;
using Caissalytics.Core;

namespace Caissalytics.Data;

public class RepertoireService : IRepertoireService
{
    private readonly string _repertoireFilePath;
    private RepertoireTree _tree = new();
    private readonly object _lock = new();

    public event Action? OnRepertoireChanged;

    public RepertoireService(string? storageDirectory = null)
    {
        string baseDir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics");
        Directory.CreateDirectory(baseDir);
        _repertoireFilePath = Path.Combine(baseDir, "repertoire_data.json");

        LoadRepertoire();
    }

    public Task<RepertoireTree> GetRepertoireAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(CloneTree(_tree));
        }
    }

    public Task AddOrUpdateMoveAsync(RepertoireMove move)
    {
        lock (_lock)
        {
            var list = string.Equals(move.Color, "black", StringComparison.OrdinalIgnoreCase)
                ? _tree.BlackMoves
                : _tree.WhiteMoves;

            string cleanFen = NormalizeFen(move.Fen);
            move.Fen = cleanFen;

            var existing = list.FirstOrDefault(m =>
                string.Equals(NormalizeFen(m.Fen), cleanFen, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.MoveSan, move.MoveSan, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Status = move.Status;
                existing.Note = move.Note;
                existing.MoveUci = move.MoveUci;
            }
            else
            {
                list.Add(move);
            }

            SaveRepertoire();
        }

        OnRepertoireChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task RemoveMoveAsync(string fen, string moveSan, string color)
    {
        lock (_lock)
        {
            var list = string.Equals(color, "black", StringComparison.OrdinalIgnoreCase)
                ? _tree.BlackMoves
                : _tree.WhiteMoves;

            string cleanFen = NormalizeFen(fen);
            list.RemoveAll(m =>
                string.Equals(NormalizeFen(m.Fen), cleanFen, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.MoveSan, moveSan, StringComparison.OrdinalIgnoreCase));

            SaveRepertoire();
        }

        OnRepertoireChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task<List<RepertoireMove>> GetMovesForPositionAsync(string fen, string? color = null)
    {
        lock (_lock)
        {
            string cleanFen = NormalizeFen(fen);
            var result = new List<RepertoireMove>();

            if (color == null || string.Equals(color, "white", StringComparison.OrdinalIgnoreCase))
            {
                result.AddRange(_tree.WhiteMoves.Where(m => string.Equals(NormalizeFen(m.Fen), cleanFen, StringComparison.OrdinalIgnoreCase)));
            }

            if (color == null || string.Equals(color, "black", StringComparison.OrdinalIgnoreCase))
            {
                result.AddRange(_tree.BlackMoves.Where(m => string.Equals(NormalizeFen(m.Fen), cleanFen, StringComparison.OrdinalIgnoreCase)));
            }

            return Task.FromResult(result);
        }
    }

    public Task<string> ExportRepertoireToPgnAsync(string color)
    {
        lock (_lock)
        {
            bool isBlack = string.Equals(color, "black", StringComparison.OrdinalIgnoreCase);
            var moves = isBlack ? _tree.BlackMoves : _tree.WhiteMoves;

            var sb = new StringBuilder();
            sb.AppendLine($"[Event \"Caissalytics Personal {(isBlack ? "Black" : "White")} Repertoire\"]");
            sb.AppendLine($"[Site \"Caissalytics Desktop\"]");
            sb.AppendLine($"[Date \"{DateTime.UtcNow:yyyy.MM.dd}\"]");
            sb.AppendLine($"[Round \"-\"]");
            sb.AppendLine($"[White \"{(isBlack ? "Opponent" : "Personal Repertoire")}\"]");
            sb.AppendLine($"[Black \"{(isBlack ? "Personal Repertoire" : "Opponent")}\"]");
            sb.AppendLine($"[Result \"*\"]");
            sb.AppendLine();

            foreach (var m in moves)
            {
                sb.AppendLine($"{{FEN: {m.Fen}}} {m.MoveSan} $1 {{{(!string.IsNullOrWhiteSpace(m.Note) ? m.Note : m.Status)}}}");
            }

            sb.AppendLine("*");
            return Task.FromResult(sb.ToString());
        }
    }

    private void LoadRepertoire()
    {
        lock (_lock)
        {
            if (File.Exists(_repertoireFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_repertoireFilePath);
                    var t = JsonSerializer.Deserialize<RepertoireTree>(json);
                    if (t != null)
                    {
                        _tree = t;
                        return;
                    }
                }
                catch { }
            }

            // Pre-seed default repertoire entries
            SeedDefaultRepertoire();
            SaveRepertoire();
        }
    }

    private void SeedDefaultRepertoire()
    {
        string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        // White: 1. e4 (King's Pawn Opening)
        _tree.WhiteMoves.Add(new RepertoireMove
        {
            Fen = NormalizeFen(startFen),
            MoveSan = "e4",
            Color = "white",
            Status = "main",
            Note = "Open tactical games. Controls central d5 and f5 squares."
        });

        // Black: 1... c5 (Sicilian Defense) against 1. e4
        string e4Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
        _tree.BlackMoves.Add(new RepertoireMove
        {
            Fen = NormalizeFen(e4Fen),
            MoveSan = "c5",
            Color = "black",
            Status = "main",
            Note = "Sicilian Defense: fighting for the center with an asymmetrical pawn structure."
        });
    }

    private void SaveRepertoire()
    {
        try
        {
            string json = JsonSerializer.Serialize(_tree, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_repertoireFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RepertoireService] Save error: {ex.Message}");
        }
    }

    string IRepertoireService.NormalizeFen(string fen) => NormalizeFen(fen);

    public static string NormalizeFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return "";
        var parts = fen.Trim().Split(' ');
        // Keep piece placement, active color, castling, en passant
        if (parts.Length >= 4)
        {
            return $"{parts[0]} {parts[1]} {parts[2]} {parts[3]}";
        }
        return parts[0];
    }

    private static RepertoireTree CloneTree(RepertoireTree src)
    {
        return new RepertoireTree
        {
            WhiteMoves = src.WhiteMoves.Select(m => new RepertoireMove
            {
                Id = m.Id,
                Fen = m.Fen,
                MoveSan = m.MoveSan,
                MoveUci = m.MoveUci,
                Color = m.Color,
                Status = m.Status,
                Note = m.Note,
                CreatedAt = m.CreatedAt
            }).ToList(),
            BlackMoves = src.BlackMoves.Select(m => new RepertoireMove
            {
                Id = m.Id,
                Fen = m.Fen,
                MoveSan = m.MoveSan,
                MoveUci = m.MoveUci,
                Color = m.Color,
                Status = m.Status,
                Note = m.Note,
                CreatedAt = m.CreatedAt
            }).ToList()
        };
    }
}
