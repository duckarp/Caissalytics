using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Caissalytics.Core;

namespace Caissalytics.Data;

public class TablebaseService : ITablebaseService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<string, TablebaseResult> _cache = new();
    private readonly string _configFilePath;
    private string? _localSyzygyPath;
    private bool _isOnlineProbeEnabled = true;

    public event Action? OnSettingsChanged;

    public string? LocalSyzygyPath
    {
        get => _localSyzygyPath;
        set
        {
            if (_localSyzygyPath != value)
            {
                _localSyzygyPath = value;
                SaveConfig();
                OnSettingsChanged?.Invoke();
            }
        }
    }

    public bool IsOnlineProbeEnabled
    {
        get => _isOnlineProbeEnabled;
        set
        {
            if (_isOnlineProbeEnabled != value)
            {
                _isOnlineProbeEnabled = value;
                SaveConfig();
                OnSettingsChanged?.Invoke();
            }
        }
    }

    public TablebaseService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string configDir = Path.Combine(localAppData, "Caissalytics");
        Directory.CreateDirectory(configDir);
        _configFilePath = Path.Combine(configDir, "tablebase_config.json");

        LoadConfig();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var dto = JsonSerializer.Deserialize<TablebaseConfigDto>(json);
                if (dto != null)
                {
                    _localSyzygyPath = dto.LocalSyzygyPath;
                    _isOnlineProbeEnabled = dto.IsOnlineProbeEnabled;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TablebaseService] Error reading config: {ex.Message}");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var dto = new TablebaseConfigDto
            {
                LocalSyzygyPath = _localSyzygyPath,
                IsOnlineProbeEnabled = _isOnlineProbeEnabled
            };
            string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TablebaseService] Error saving config: {ex.Message}");
        }
    }

    public int CountPieces(BoardPosition position)
    {
        int count = 0;
        for (int i = 0; i < 64; i++)
        {
            if (position.Squares[i].Type != PieceType.None)
                count++;
        }
        return count;
    }

    public static int CountPiecesInFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return 0;
        string placement = fen.Trim().Split(' ')[0];
        int count = 0;
        foreach (char c in placement)
        {
            if (char.IsLetter(c)) count++;
        }
        return count;
    }

    public bool CanProbePosition(BoardPosition position)
    {
        return CountPieces(position) <= 7;
    }

    public bool CanProbeFen(string fen)
    {
        return CountPiecesInFen(fen) <= 7;
    }

    public Task<TablebaseResult?> ProbePositionAsync(BoardPosition position, CancellationToken ct = default)
    {
        string fen = FenParser.ToFen(position);
        return ProbePositionAsync(fen, ct);
    }

    public async Task<TablebaseResult?> ProbePositionAsync(string fen, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fen)) return null;

        string normalizedFen = NormalizeFenForProbe(fen);
        if (!CanProbeFen(normalizedFen))
            return null;

        if (_cache.TryGetValue(normalizedFen, out var cached))
            return cached;

        if (!_isOnlineProbeEnabled)
            return null;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            string url = $"https://tablebase.lichess.ovh/standard?fen={Uri.EscapeDataString(normalizedFen)}";
            var response = await _httpClient.GetFromJsonAsync<LichessTablebaseApiResponse>(url, timeoutCts.Token);
            if (response == null) return null;

            var result = MapApiResponse(normalizedFen, response);
            _cache.TryAdd(normalizedFen, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TablebaseResult
            {
                Fen = normalizedFen,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string NormalizeFenForProbe(string fen)
    {
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return fen.Trim();

        // Standardize to placement, active color, castling, en-passant, halfmove (0), fullmove (1)
        string placement = parts[0];
        string active = parts[1];
        string castling = parts[2];
        string ep = parts[3];
        return $"{placement} {active} {castling} {ep} 0 1";
    }

    private static TablebaseResult MapApiResponse(string fen, LichessTablebaseApiResponse api)
    {
        var result = new TablebaseResult
        {
            Fen = fen,
            Category = ParseCategory(api.Category),
            Dtz = api.Dtz,
            Dtm = api.Dtm,
            Checkmate = api.Checkmate,
            Stalemate = api.Stalemate,
            InsufficientMaterial = api.InsufficientMaterial,
            Success = true
        };

        if (api.Moves != null)
        {
            foreach (var m in api.Moves)
            {
                var moveCategory = InvertCategory(ParseCategory(m.Category));
                result.Moves.Add(new TablebaseMove
                {
                    Uci = m.Uci ?? string.Empty,
                    San = m.San ?? string.Empty,
                    Category = moveCategory,
                    Dtz = m.Dtz.HasValue ? -m.Dtz.Value : null,
                    Dtm = m.Dtm.HasValue ? -m.Dtm.Value : null,
                    IsZeroing = m.Zeroing,
                    IsCheckmate = m.Checkmate,
                    IsStalemate = m.Stalemate,
                    IsConversion = m.Conversion
                });
            }
        }

        // Sort moves: best winning moves first, then draws, then losses
        result.Moves.Sort((a, b) =>
        {
            int rankA = GetCategoryRank(a.Category);
            int rankB = GetCategoryRank(b.Category);
            if (rankA != rankB) return rankB.CompareTo(rankA);

            // If both win, smaller positive DTM or DTZ is faster win
            if (a.Category == TablebaseCategory.Win)
            {
                int valA = a.Dtm ?? a.Dtz ?? 999;
                int valB = b.Dtm ?? b.Dtz ?? 999;
                return Math.Abs(valA).CompareTo(Math.Abs(valB));
            }

            // If both lose, longer distance is more resilient defense
            if (a.Category == TablebaseCategory.Loss)
            {
                int valA = a.Dtm ?? a.Dtz ?? 0;
                int valB = b.Dtm ?? b.Dtz ?? 0;
                return Math.Abs(valB).CompareTo(Math.Abs(valA));
            }

            return 0;
        });

        return result;
    }

    private static int GetCategoryRank(TablebaseCategory cat)
    {
        return cat switch
        {
            TablebaseCategory.Checkmate => 10,
            TablebaseCategory.Win => 9,
            TablebaseCategory.CursedWin => 6,
            TablebaseCategory.Draw => 5,
            TablebaseCategory.BlessedLoss => 4,
            TablebaseCategory.Loss => 1,
            TablebaseCategory.Stalemate => 5,
            _ => 0
        };
    }

    private static TablebaseCategory ParseCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return TablebaseCategory.Unknown;
        return raw.ToLowerInvariant() switch
        {
            "win" => TablebaseCategory.Win,
            "draw" => TablebaseCategory.Draw,
            "loss" => TablebaseCategory.Loss,
            "cursed-win" => TablebaseCategory.CursedWin,
            "blessed-loss" => TablebaseCategory.BlessedLoss,
            "maybe-win" => TablebaseCategory.Win,
            "maybe-loss" => TablebaseCategory.Loss,
            _ => TablebaseCategory.Unknown
        };
    }

    private static TablebaseCategory InvertCategory(TablebaseCategory opponentAfterMove)
    {
        // When Lichess returns move.category, it is from the opponent's view AFTER the move.
        return opponentAfterMove switch
        {
            TablebaseCategory.Loss => TablebaseCategory.Win,
            TablebaseCategory.Win => TablebaseCategory.Loss,
            TablebaseCategory.BlessedLoss => TablebaseCategory.CursedWin,
            TablebaseCategory.CursedWin => TablebaseCategory.BlessedLoss,
            TablebaseCategory.Draw => TablebaseCategory.Draw,
            _ => opponentAfterMove
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private class TablebaseConfigDto
    {
        public string? LocalSyzygyPath { get; set; }
        public bool IsOnlineProbeEnabled { get; set; } = true;
    }

    private class LichessTablebaseApiResponse
    {
        [JsonPropertyName("checkmate")] public bool Checkmate { get; set; }
        [JsonPropertyName("stalemate")] public bool Stalemate { get; set; }
        [JsonPropertyName("insufficient_material")] public bool InsufficientMaterial { get; set; }
        [JsonPropertyName("dtz")] public int? Dtz { get; set; }
        [JsonPropertyName("dtm")] public int? Dtm { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("moves")] public List<LichessMoveDto>? Moves { get; set; }
    }

    private class LichessMoveDto
    {
        [JsonPropertyName("uci")] public string? Uci { get; set; }
        [JsonPropertyName("san")] public string? San { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("dtz")] public int? Dtz { get; set; }
        [JsonPropertyName("dtm")] public int? Dtm { get; set; }
        [JsonPropertyName("zeroing")] public bool Zeroing { get; set; }
        [JsonPropertyName("conversion")] public bool Conversion { get; set; }
        [JsonPropertyName("checkmate")] public bool Checkmate { get; set; }
        [JsonPropertyName("stalemate")] public bool Stalemate { get; set; }
    }
}
