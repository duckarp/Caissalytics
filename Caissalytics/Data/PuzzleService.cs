using System.Text.Json;
using Caissalytics.Core;

namespace Caissalytics.Data;

public class PuzzleService : IPuzzleService
{
    private readonly IDatabaseService _databaseService;
    private readonly IUserProfileService _profileService;
    private readonly string _statsFilePath;
    private readonly string _extractedPuzzlesPath;
    private PuzzleStats _stats = new();
    private readonly List<ChessPuzzle> _cachedExtractedPuzzles = new();
    private readonly object _lock = new();

    public event Action? OnPuzzleStatsChanged;

    public PuzzleService(IDatabaseService databaseService, IUserProfileService profileService, string? storageDirectory = null)
    {
        _databaseService = databaseService;
        _profileService = profileService;

        string baseDir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics");
        Directory.CreateDirectory(baseDir);
        _statsFilePath = Path.Combine(baseDir, "puzzles_progress.json");
        _extractedPuzzlesPath = Path.Combine(baseDir, "extracted_puzzles.json");

        LoadStats();
        LoadExtractedPuzzles();
    }

    public Task<PuzzleStats> GetStatsAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(CloneStats(_stats));
        }
    }

    public Task SaveStatsAsync(PuzzleStats stats)
    {
        lock (_lock)
        {
            _stats = CloneStats(stats);
            try
            {
                string json = JsonSerializer.Serialize(_stats, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_statsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PuzzleService] Error saving stats: {ex.Message}");
            }
        }

        OnPuzzleStatsChanged?.Invoke();
        return Task.CompletedTask;
    }

    public async Task<PuzzleStats> RecordAttemptAsync(string puzzleId, bool isSuccess, int timeSpentSeconds)
    {
        lock (_lock)
        {
            int delta;
            if (isSuccess)
            {
                _stats.SolvedCount++;
                _stats.CurrentStreak++;
                if (_stats.CurrentStreak > _stats.BestStreak)
                {
                    _stats.BestStreak = _stats.CurrentStreak;
                }

                // Base +15, small streak bonus
                delta = _stats.CurrentStreak >= 3 ? 18 : 15;
                _stats.CurrentRating += delta;
                _stats.SolvedPuzzleIds.Add(puzzleId);
                _stats.FailedPuzzleIds.Remove(puzzleId);
            }
            else
            {
                _stats.FailedCount++;
                _stats.CurrentStreak = 0;
                delta = -15;
                _stats.CurrentRating = Math.Max(400, _stats.CurrentRating + delta);
                _stats.FailedPuzzleIds.Add(puzzleId);
            }

            _stats.History.Add(new PuzzleAttempt
            {
                PuzzleId = puzzleId,
                SolvedAt = DateTime.UtcNow,
                IsSuccess = isSuccess,
                TimeSpentSeconds = timeSpentSeconds,
                RatingDelta = delta,
                NewRating = _stats.CurrentRating
            });

            // Keep history trimmed to last 200 attempts
            if (_stats.History.Count > 200)
            {
                _stats.History.RemoveRange(0, _stats.History.Count - 200);
            }

            try
            {
                string json = JsonSerializer.Serialize(_stats, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_statsFilePath, json);
            }
            catch { }
        }

        OnPuzzleStatsChanged?.Invoke();
        return await GetStatsAsync();
    }

    public async Task<List<ChessPuzzle>> GetPuzzlesAsync(PuzzleFilterOptions filter, CancellationToken ct = default)
    {
        var result = new List<ChessPuzzle>();

        // 1. Curated puzzles
        if (filter.Mode == "all" || filter.Mode == "curated")
        {
            result.AddRange(GetCuratedPuzzles());
        }

        // 2. Extracted user blunders
        if (filter.Mode == "all" || filter.Mode == "blunders")
        {
            lock (_lock)
            {
                result.AddRange(_cachedExtractedPuzzles);
            }
        }

        // 3. Needs review (failed previously)
        if (filter.Mode == "review")
        {
            lock (_lock)
            {
                var failedIds = _stats.FailedPuzzleIds;
                var all = GetCuratedPuzzles().Concat(_cachedExtractedPuzzles);
                result.AddRange(all.Where(p => failedIds.Contains(p.Id)));
            }
        }

        // Auto-extract if no blunders exist yet and blunders were requested
        if (result.Count == 0 && (filter.Mode == "all" || filter.Mode == "blunders"))
        {
            await TryAutoExtractFromOnlineGamesAsync(ct);
            lock (_lock)
            {
                result.AddRange(_cachedExtractedPuzzles);
            }
            if (result.Count == 0 && filter.Mode == "all")
            {
                result.AddRange(GetCuratedPuzzles());
            }
        }

        return result;
    }

    private async Task TryAutoExtractFromOnlineGamesAsync(CancellationToken ct)
    {
        try
        {
            var dbs = await _databaseService.GetDatabasesAsync();
            var onlineDb = dbs.FirstOrDefault(d => string.Equals(d.Name, "My online games", StringComparison.OrdinalIgnoreCase));
            if (onlineDb != null && onlineDb.GameCount > 0)
            {
                await ExtractPuzzlesFromDatabaseAsync(onlineDb.Name, ct);
            }
            else
            {
                var active = await _databaseService.GetActiveDatabaseAsync();
                if (active != null && active.GameCount > 0)
                {
                    await ExtractPuzzlesFromDatabaseAsync(active.Name, ct);
                }
            }
        }
        catch { }
    }

    public async Task<int> ExtractPuzzlesFromDatabaseAsync(string databaseName, CancellationToken ct = default)
    {
        var headers = await _databaseService.GetAllGameHeadersAsync(databaseName);
        if (headers.Count == 0) return 0;

        var profile = await _profileService.GetProfileAsync();
        int extractedCount = 0;
        var newPuzzles = new List<ChessPuzzle>();

        foreach (var h in headers.Take(200)) // Cap per batch to maintain lightning responsiveness
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(h.Pgn)) continue;

            try
            {
                var tree = PgnHandler.ImportPgn(h.Pgn);
                bool isUserWhite = profile.MatchesPlayer(h.White);
                bool isUserBlack = !isUserWhite && profile.MatchesPlayer(h.Black);

                // Traverse mainline to find moves with NAG 2 (mistake) or 4 (blunder) or comments with best variation
                var curr = tree.Root;
                int ply = 0;
                while (curr.Children.Count > 0)
                {
                    var child = curr.Children[0];
                    ply++;
                    bool isWhite = (ply % 2 == 1);

                    // Check if this move is a blunder or mistake
                    bool isBlunder = child.Nags.Contains(4);
                    bool isMistake = child.Nags.Contains(2);
                    bool hasVariationAlternative = curr.Children.Count > 1;

                    // If annotated with variation alternative or NAG
                    if ((isBlunder || isMistake || hasVariationAlternative) && curr.Children.Count > 1)
                    {
                        var altChild = curr.Children[1];
                        if (altChild.Nags.Contains(1) || (!string.IsNullOrEmpty(altChild.Comment) && altChild.Comment.Contains("Best", StringComparison.OrdinalIgnoreCase)) || isBlunder || isMistake)
                        {
                            // We found a blunder position! The puzzle starts at curr.Position!
                            string fen = FenParser.ToFen(curr.Position);
                            string turn = isWhite ? "white" : "black";

                            var solutionMoves = new List<string> { altChild.San };
                            var continuation = altChild;
                            int steps = 0;
                            while (continuation.Children.Count > 0 && steps < 3)
                            {
                                continuation = continuation.Children[0];
                                solutionMoves.Add(continuation.San);
                                steps++;
                            }

                            string playerName = isWhite ? h.White : h.Black;
                            string oppName = isWhite ? h.Black : h.White;

                            string blunderNotice = isBlunder ? "blundered with" : "played";
                            string explanation = $"In the game, {playerName} {blunderNotice} {child.San}. The winning tactical idea was {altChild.San}!";

                            var puzzle = new ChessPuzzle
                            {
                                Id = $"user_{h.Id}_{ply}",
                                Title = $"Blunder in {h.White} vs {h.Black}",
                                Source = string.Equals(databaseName, "My online games", StringComparison.OrdinalIgnoreCase) ? "My Online Games" : databaseName,
                                GameId = h.Id,
                                DatabaseName = databaseName,
                                WhitePlayer = h.White,
                                BlackPlayer = h.Black,
                                Date = h.Date,
                                Opening = OpeningCatalog.ResolveOpeningName(h.Eco, h.Pgn),
                                Event = h.Event,
                                Fen = fen,
                                PlayerColor = turn,
                                PlayedBlunderSan = child.San,
                                SolutionMovesSan = solutionMoves,
                                Explanation = explanation,
                                Rating = isBlunder ? 1400 : 1600,
                                Themes = new List<string> { "Personal Blunder", isWhite ? "White to move" : "Black to move" },
                                IsUserBlunder = isUserWhite || isUserBlack
                            };

                            newPuzzles.Add(puzzle);
                            extractedCount++;
                            if (newPuzzles.Count >= 50) break;
                        }
                    }

                    curr = child;
                }
            }
            catch
            {
                // Continue scanning remaining games
            }

            if (newPuzzles.Count >= 50) break;
        }

        if (newPuzzles.Count > 0)
        {
            lock (_lock)
            {
                var existingIds = _cachedExtractedPuzzles.Select(p => p.Id).ToHashSet();
                foreach (var p in newPuzzles)
                {
                    if (!existingIds.Contains(p.Id))
                    {
                        _cachedExtractedPuzzles.Add(p);
                    }
                }
                SaveExtractedPuzzles();
            }
        }

        return extractedCount;
    }

    private void LoadStats()
    {
        lock (_lock)
        {
            if (File.Exists(_statsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_statsFilePath);
                    var s = JsonSerializer.Deserialize<PuzzleStats>(json);
                    if (s != null) _stats = s;
                }
                catch { }
            }
        }
    }

    private void LoadExtractedPuzzles()
    {
        lock (_lock)
        {
            if (File.Exists(_extractedPuzzlesPath))
            {
                try
                {
                    string json = File.ReadAllText(_extractedPuzzlesPath);
                    var p = JsonSerializer.Deserialize<List<ChessPuzzle>>(json);
                    if (p != null)
                    {
                        _cachedExtractedPuzzles.Clear();
                        _cachedExtractedPuzzles.AddRange(p);
                    }
                }
                catch { }
            }
        }
    }

    private void SaveExtractedPuzzles()
    {
        try
        {
            string json = JsonSerializer.Serialize(_cachedExtractedPuzzles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_extractedPuzzlesPath, json);
        }
        catch { }
    }

    private static PuzzleStats CloneStats(PuzzleStats src)
    {
        return new PuzzleStats
        {
            CurrentRating = src.CurrentRating,
            SolvedCount = src.SolvedCount,
            FailedCount = src.FailedCount,
            CurrentStreak = src.CurrentStreak,
            BestStreak = src.BestStreak,
            SolvedPuzzleIds = new HashSet<string>(src.SolvedPuzzleIds),
            FailedPuzzleIds = new HashSet<string>(src.FailedPuzzleIds),
            History = new List<PuzzleAttempt>(src.History)
        };
    }

    public List<ChessPuzzle> GetCuratedPuzzles()
    {
        return new List<ChessPuzzle>
        {
            new()
            {
                Id = "curated_reti_1910",
                Title = "Richard Réti vs Savielly Tartakower (1910)",
                Source = "Vienna Master Tournament",
                WhitePlayer = "Richard Réti",
                BlackPlayer = "Savielly Tartakower",
                Date = "1910.??.??",
                Opening = "Caro-Kann Defense (B10)",
                Event = "Vienna 1910",
                Fen = "r1b1kb1r/pp3ppp/2p5/4q3/4n3/3Q4/PPPB1PPP/2KR1BNR w kq - 0 9",
                PlayerColor = "white",
                OpponentMoveLeadingIn = "8... Nxe4??",
                PlayedBlunderSan = "Nxe4??",
                SolutionMovesSan = new List<string> { "Qd8+", "Kxd8", "Bg5+" },
                Explanation = "In the game, Tartakower fell into Réti's legendary trap with 8... Nxe4??. White delivers a magnificent queen sacrifice with 9. Qd8+!! Kxd8 10. Bg5+ (double check!), forcing mate after 10... Ke8 11. Rd8# or 10... Kc7 11. Bd8#.",
                Rating = 1550,
                Themes = new List<string> { "Queen Sacrifice", "Double Check", "Checkmate" },
                IsUserBlunder = false
            },
            new()
            {
                Id = "curated_morphy_opera",
                Title = "Paul Morphy vs Duke of Brunswick & Count Isouard",
                Source = "Paris Opera Game (1858)",
                WhitePlayer = "Paul Morphy",
                BlackPlayer = "Duke / Count Isouard",
                Date = "1858.11.02",
                Opening = "Philidor Defense (C41)",
                Event = "Paris Opera 1858",
                Fen = "4kb1r/p2n1ppp/4q3/6B1/8/1Q6/PPP2PPP/2KR4 w k - 0 16",
                PlayerColor = "white",
                OpponentMoveLeadingIn = "15... Nxd7",
                PlayedBlunderSan = "Nxd7",
                SolutionMovesSan = new List<string> { "Qb8+", "Nxb8", "Rd8#" },
                Explanation = "Morphy finishes with the immortal queen sacrifice 16. Qb8+! After 16... Nxb8, 17. Rd8# delivers checkmate with the bishop on g5 defending the rook.",
                Rating = 1450,
                Themes = new List<string> { "Deflection", "Back Rank Mate", "Sacrifice" },
                IsUserBlunder = false
            },
            new()
            {
                Id = "curated_navara_fork",
                Title = "David Navara Tactical Fork",
                Source = "Czech Championship",
                WhitePlayer = "David Navara",
                BlackPlayer = "Master Opponent",
                Date = "2013.??.??",
                Opening = "Sicilian Defense",
                Event = "Czech Championship",
                Fen = "r1b2rk1/pp3ppp/8/2p1N3/2B5/8/PPP2PPP/2KR4 w - - 0 17",
                PlayerColor = "white",
                OpponentMoveLeadingIn = "16... c5?",
                PlayedBlunderSan = "c5?",
                SolutionMovesSan = new List<string> { "Bxf7+", "Rxf7", "Rd8+" },
                Explanation = "White exploits the weak f7 square and loose back rank with 17. Bxf7+! Rxf7 18. Rd8+ Rf8 19. Rxf8# winning material and mating.",
                Rating = 1500,
                Themes = new List<string> { "Tactics", "Weak Square", "Back Rank" },
                IsUserBlunder = false
            },
            new()
            {
                Id = "curated_smothered_mate",
                Title = "Classic Smothered Mate (Philidor's Legacy)",
                Source = "Tactical Master Study",
                WhitePlayer = "White",
                BlackPlayer = "Black",
                Date = "1790.??.??",
                Opening = "Tactical Study",
                Event = "Study",
                Fen = "6k1/5Npp/8/8/8/8/1Q4PP/4R1K1 w - - 0 1",
                PlayerColor = "white",
                OpponentMoveLeadingIn = "1... h7-h6?",
                PlayedBlunderSan = "h6?",
                SolutionMovesSan = new List<string> { "Re8#" },
                Explanation = "The back rank is unguarded. 1. Re8# terminates the game on the spot!",
                Rating = 1350,
                Themes = new List<string> { "Back Rank", "Checkmate" },
                IsUserBlunder = false
            },
            new()
            {
                Id = "curated_movsesian_pin",
                Title = "Sergei Movsesian vs Grandmaster Tactic",
                Source = "Slovak Extraliga Master Game",
                WhitePlayer = "Sergei Movsesian",
                BlackPlayer = "GM Opponent",
                Date = "2008.??.??",
                Opening = "French Defense",
                Event = "Slovak Extraliga",
                Fen = "r1b1k2r/pp1n1ppp/4p3/3pP3/1b1q1P2/3B4/PP1N2PP/R1BQK2R w KQkq - 0 11",
                PlayerColor = "white",
                OpponentMoveLeadingIn = "10... Qxd4?",
                PlayedBlunderSan = "Qxd4?",
                SolutionMovesSan = new List<string> { "Qe2" },
                Explanation = "Defending the d3 bishop while maintaining superior piece coordination and preparing Queenside castling.",
                Rating = 1400,
                Themes = new List<string> { "Defense", "Piece Play" },
                IsUserBlunder = false
            },
            new()
            {
                Id = "curated_fork_tricks",
                Title = "Royal Knight Fork Tactic",
                Source = "Tactical Master Study",
                WhitePlayer = "White",
                BlackPlayer = "Black",
                Date = "2020.??.??",
                Opening = "King's Indian Attack",
                Event = "Master Study",
                Fen = "r1b1kb1r/pppp1ppp/5n2/4q3/4P3/2N5/PPP2PPP/R1BQKB1R w KQkq - 0 6",
                PlayerColor = "white",
                OpponentMoveLeadingIn = "5... Qxe5?",
                PlayedBlunderSan = "Qxe5?",
                SolutionMovesSan = new List<string> { "Qe2" },
                Explanation = "Pinning the Queen to the Black King along the e-file!",
                Rating = 1300,
                Themes = new List<string> { "Pin", "Queen Pin" },
                IsUserBlunder = false
            }
        };
    }
}
