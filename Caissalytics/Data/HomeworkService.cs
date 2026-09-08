using System.Text.Json;

namespace Caissalytics.Data;

public class HomeworkService : IHomeworkService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<HomeworkSheet> _sheets = new();

    public event Action? OnSheetsChanged;

    public HomeworkService(string? storageDirectory = null)
    {
        string baseDir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics");
        Directory.CreateDirectory(baseDir);
        _filePath = Path.Combine(baseDir, "homework_sheets.json");

        LoadSheets();
    }

    public Task<List<HomeworkSheet>> GetSheetsAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(_sheets.OrderByDescending(s => s.UpdatedAt).ToList());
        }
    }

    public Task<HomeworkSheet?> GetSheetAsync(string id)
    {
        lock (_lock)
        {
            var sheet = _sheets.FirstOrDefault(s => s.Id == id);
            return Task.FromResult(sheet != null ? CloneSheet(sheet) : null);
        }
    }

    public Task SaveSheetAsync(HomeworkSheet sheet)
    {
        lock (_lock)
        {
            sheet.UpdatedAt = DateTime.UtcNow;
            int idx = _sheets.FindIndex(s => s.Id == sheet.Id);
            if (idx >= 0)
            {
                _sheets[idx] = CloneSheet(sheet);
            }
            else
            {
                _sheets.Insert(0, CloneSheet(sheet));
            }
            SaveSheets();
        }

        OnSheetsChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteSheetAsync(string id)
    {
        lock (_lock)
        {
            _sheets.RemoveAll(s => s.Id == id);
            SaveSheets();
        }

        OnSheetsChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task<HomeworkSheet> DuplicateSheetAsync(string id)
    {
        HomeworkSheet cloned;
        lock (_lock)
        {
            var original = _sheets.FirstOrDefault(s => s.Id == id);
            if (original == null)
            {
                throw new ArgumentException($"Homework sheet with ID '{id}' not found.");
            }

            cloned = CloneSheet(original);
            cloned.Id = Guid.NewGuid().ToString("N");
            cloned.Title = $"{original.Title} (Copy)";
            cloned.CreatedAt = DateTime.UtcNow;
            cloned.UpdatedAt = DateTime.UtcNow;

            foreach (var ex in cloned.Exercises)
            {
                ex.Id = Guid.NewGuid().ToString("N");
            }

            _sheets.Insert(0, cloned);
            SaveSheets();
        }

        OnSheetsChanged?.Invoke();
        return Task.FromResult(cloned);
    }

    public Task<HomeworkSheet> CreateNewSheetAsync(string title, int exerciseCount = 6, string templateType = "mate_in_one")
    {
        var sheet = new HomeworkSheet
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(title) ? "Chess Tactics Worksheet" : title,
            Subtitle = "Find the best move in each position. Write down your solution and key defense.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DiagramsPerRow = exerciseCount <= 4 ? 2 : 2
        };

        var templates = GetSampleExercises(templateType);
        for (int i = 0; i < exerciseCount; i++)
        {
            if (i < templates.Count)
            {
                var copy = CloneExercise(templates[i]);
                copy.Order = i + 1;
                copy.Title = $"Exercise {i + 1}";
                sheet.Exercises.Add(copy);
            }
            else
            {
                sheet.Exercises.Add(new HomeworkExercise
                {
                    Order = i + 1,
                    Title = $"Exercise {i + 1}",
                    Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                    ToMove = "white",
                    Prompt = "White to move and win",
                    Solution = ""
                });
            }
        }

        lock (_lock)
        {
            _sheets.Insert(0, sheet);
            SaveSheets();
        }

        OnSheetsChanged?.Invoke();
        return Task.FromResult(sheet);
    }

    private void LoadSheets()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonSerializer.Deserialize<List<HomeworkSheet>>(json);
                    if (loaded != null && loaded.Count > 0)
                    {
                        _sheets = loaded;
                        return;
                    }
                }
            }
            catch { }

            // Initialize default starter coaching templates if empty
            _sheets = CreateStarterTemplates();
            SaveSheets();
        }
    }

    private void SaveSheets()
    {
        try
        {
            string json = JsonSerializer.Serialize(_sheets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    private static HomeworkSheet CloneSheet(HomeworkSheet s)
    {
        return new HomeworkSheet
        {
            Id = s.Id,
            Title = s.Title,
            Subtitle = s.Subtitle,
            CoachName = s.CoachName,
            ClubName = s.ClubName,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            DiagramsPerRow = s.DiagramsPerRow,
            ShowCoordinates = s.ShowCoordinates,
            ShowStudentHeader = s.ShowStudentHeader,
            ShowSolutionLines = s.ShowSolutionLines,
            IncludeAnswerKey = s.IncludeAnswerKey,
            Exercises = s.Exercises.Select(CloneExercise).ToList()
        };
    }

    private static HomeworkExercise CloneExercise(HomeworkExercise e)
    {
        return new HomeworkExercise
        {
            Id = e.Id,
            Order = e.Order,
            Fen = e.Fen,
            ToMove = e.ToMove,
            Orientation = e.Orientation,
            Title = e.Title,
            Prompt = e.Prompt,
            Solution = e.Solution,
            Points = e.Points,
            Difficulty = e.Difficulty
        };
    }

    private static List<HomeworkExercise> GetSampleExercises(string templateType)
    {
        return templateType switch
        {
            "forks_pins" => new List<HomeworkExercise>
            {
                new() { Order = 1, Title = "Exercise 1", Fen = "r1bqk2r/pppp1ppp/2n5/4p3/1b2n3/2NP1N2/PPP1BPPP/R1BQK2R w KQkq - 0 6", ToMove = "white", Prompt = "White to move: Win a piece", Solution = "1. dxe4", Difficulty = "Easy" },
                new() { Order = 2, Title = "Exercise 2", Fen = "r1b1kb1r/pppp1ppp/5q2/4n3/3QP3/2N5/PPP2PPP/R1B1KB1R w KQkq - 1 8", ToMove = "white", Prompt = "White to move: Discover an attack on the Queen", Solution = "1. Nd5", Difficulty = "Medium" },
                new() { Order = 3, Title = "Exercise 3", Fen = "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4", ToMove = "white", Prompt = "White to move: Attack f7", Solution = "1. Ng5", Difficulty = "Easy" },
                new() { Order = 4, Title = "Exercise 4", Fen = "6k1/5ppp/8/8/8/8/1r4PP/R5K1 w - - 0 1", ToMove = "white", Prompt = "White to move: Back rank checkmate", Solution = "1. Ra8+ Rb8 2. Rxb8#", Difficulty = "Easy" }
            },
            _ => new List<HomeworkExercise>
            {
                new() { Order = 1, Title = "Exercise 1", Fen = "6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1", ToMove = "white", Prompt = "White to move: Checkmate in 1", Solution = "1. Ra8#", Difficulty = "Easy" },
                new() { Order = 2, Title = "Exercise 2", Fen = "r1bqkb1r/pppp1ppp/2n5/4p3/2B1n3/5Q2/PPPP1PPP/RNB1K1NR w KQkq - 0 5", ToMove = "white", Prompt = "White to move: Checkmate in 1 (Scholar's Mate)", Solution = "1. Qxf7#", Difficulty = "Easy" },
                new() { Order = 3, Title = "Exercise 3", Fen = "r1b1k2r/pppp1Npp/8/4p3/2Bn3q/6n1/PPPP3P/RNB2QKR b kq - 1 10", ToMove = "black", Orientation = "black", Prompt = "Black to move: Smothered checkmate in 1", Solution = "1... Nde2#", Difficulty = "Medium" },
                new() { Order = 4, Title = "Exercise 4", Fen = "7k/5K1p/6pP/8/8/8/8/8 w - - 0 1", ToMove = "white", Prompt = "White to move: Checkmate in 1", Solution = "1. Kf8 (or 1. Ke8)", Difficulty = "Easy" },
                new() { Order = 5, Title = "Exercise 5", Fen = "r4rk1/ppp2ppp/8/4p3/3q4/3B4/PPP2PPP/R2Q1RK1 w - - 0 14", ToMove = "white", Prompt = "White to move: Greek Gift Sacrifice", Solution = "1. Bxh7+ Kxh7 2. Qxd4", Difficulty = "Medium" },
                new() { Order = 6, Title = "Exercise 6", Fen = "8/8/8/8/8/5k2/4p3/4K3 b - - 0 1", ToMove = "black", Orientation = "black", Prompt = "Black to move: Winning King move", Solution = "1... Ke3 (zugzwang)", Difficulty = "Medium" }
            }
        };
    }

    private static List<HomeworkSheet> CreateStarterTemplates()
    {
        return new List<HomeworkSheet>
        {
            new()
            {
                Id = "starter-mate-in-one",
                Title = "Checkmate in 1 Move - Beginner Drill",
                Subtitle = "Look for undefended king escape squares and deliver checkmate in a single move.",
                CoachName = "Coach",
                ClubName = "Chess Club",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DiagramsPerRow = 2,
                ShowCoordinates = true,
                ShowStudentHeader = true,
                ShowSolutionLines = true,
                IncludeAnswerKey = true,
                Exercises = GetSampleExercises("mate_in_one")
            },
            new()
            {
                Id = "starter-forks-pins",
                Title = "Forks & Double Attacks - Tactical Drill",
                Subtitle = "Identify vulnerable pieces and apply tactical motifs to gain material advantage.",
                CoachName = "Coach",
                ClubName = "Chess Club",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DiagramsPerRow = 2,
                ShowCoordinates = true,
                ShowStudentHeader = true,
                ShowSolutionLines = true,
                IncludeAnswerKey = true,
                Exercises = GetSampleExercises("forks_pins")
            }
        };
    }
}
