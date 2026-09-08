using Caissalytics.Core;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class PuzzleTests
{
#pragma warning disable CS0067
    private class FakeDatabaseService : IDatabaseService
    {
        public List<GameHeader> Headers { get; set; } = new();
        public event Action? OnActiveDatabaseChanged;
        public event Action? OnReferenceDatabaseChanged;
        public event Action<string>? OnDatabaseModified;

        public Task<List<GameHeader>> GetAllGameHeadersAsync(string? databaseName = null) => Task.FromResult(Headers);
        public Task<List<DatabaseInfo>> GetDatabasesAsync() => Task.FromResult(new List<DatabaseInfo> { new() { Name = "My online games", GameCount = Headers.Count } });
        public Task<DatabaseInfo> GetActiveDatabaseAsync() => Task.FromResult(new DatabaseInfo { Name = "Default" });
        public Task SetActiveDatabaseAsync(string name) => Task.CompletedTask;
        public Task<string> GetReferenceDatabaseAsync() => Task.FromResult("ClassicalMasters");
        public Task SetReferenceDatabaseAsync(string name) => Task.CompletedTask;
        public Task<DatabaseInfo> CreateDatabaseAsync(string name) => Task.FromResult(new DatabaseInfo { Name = name });
        public Task<bool> DeleteDatabaseAsync(string name) => Task.FromResult(true);
        public Task<List<MasterCatalogItem>> GetMasterCatalogAsync() => Task.FromResult(new List<MasterCatalogItem>());
        public Task InstallMasterDatabaseAsync(string catalogId, IProgress<(int current, int total, string status)>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PositionReferenceResult> QueryPositionAsync(string? databaseName, ulong zobristKey, int maxGames = 25) => Task.FromResult(new PositionReferenceResult());
        public Task<(List<GameHeader> Games, int TotalCount)> SearchGamesAsync(string? databaseName, GameFilter filter) => Task.FromResult((Headers, Headers.Count));
        public Task<GameHeader?> GetGameByIdAsync(string? databaseName, long gameId) => Task.FromResult(Headers.FirstOrDefault(h => h.Id == gameId));
        public Task<long> SaveGameAsync(string databaseName, GameHeader game) => Task.FromResult(game.Id);
        public Task<bool> DeleteGameAsync(string databaseName, long gameId) => Task.FromResult(true);
        public Task ImportPgnStreamAsync(string databaseName, Stream stream, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default, bool deduplicate = false, bool allowProtectedDatabase = false) => Task.CompletedTask;
        public Task ImportPgnTextAsync(string databaseName, string pgnText, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default, bool deduplicate = false, bool allowProtectedDatabase = false) => Task.CompletedTask;
    }

    private class FakeUserProfileService : IUserProfileService
    {
        public UserProfile Profile { get; set; } = new()
        {
            FirstName = "Jan",
            LastName = "Testovaci",
            LichessUsername = "jantest"
        };
        public event Action? OnProfileChanged;
        public Task<UserProfile> GetProfileAsync() => Task.FromResult(Profile);
        public Task SaveProfileAsync(UserProfile profile) { Profile = profile; return Task.CompletedTask; }
    }
#pragma warning restore CS0067

    [Fact]
    public void CuratedPuzzles_AllHaveValidFensAndLegalSolutionMoves()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "puzzles_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var db = new FakeDatabaseService();
            var profile = new FakeUserProfileService();
            var service = new PuzzleService(db, profile, tempDir);

            var curated = service.GetCuratedPuzzles();
            Assert.NotEmpty(curated);

            foreach (var puzzle in curated)
            {
                // Verify starting position FEN parses correctly
                var pos = FenParser.Parse(puzzle.Fen);
                Assert.False(string.IsNullOrEmpty(puzzle.Fen));
                Assert.NotEmpty(puzzle.SolutionMovesSan);

                // Verify each solution move can be played legally in sequence
                var currentPos = pos;
                foreach (var san in puzzle.SolutionMovesSan)
                {
                    var move = SanParser.ParseSan(currentPos, san);
                    Assert.False(move.IsEmpty, $"Move '{san}' should be legal in puzzle '{puzzle.Title}' from position {FenParser.ToFen(currentPos)}");

                    var legalMoves = MoveGenerator.GenerateLegalMoves(currentPos);
                    Assert.Contains(legalMoves, m => m.From == move.From && m.To == move.To && m.Promotion == move.Promotion);

                    currentPos = MoveGenerator.ApplyMove(currentPos, move);
                }
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RecordAttempt_Success_IncreasesRatingAndTracksStreak()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "puzzles_test_" + Guid.NewGuid().ToString("N"));
        var db = new FakeDatabaseService();
        var profile = new FakeUserProfileService();
        var service = new PuzzleService(db, profile, tempDir);

        try
        {
            var initialStats = await service.GetStatsAsync();
            int startRating = initialStats.CurrentRating;

            // Record 1st success
            var stats1 = await service.RecordAttemptAsync("p1", isSuccess: true, timeSpentSeconds: 5);
            Assert.Equal(1, stats1.SolvedCount);
            Assert.Equal(0, stats1.FailedCount);
            Assert.Equal(1, stats1.CurrentStreak);
            Assert.True(stats1.CurrentRating > startRating);
            Assert.Contains("p1", stats1.SolvedPuzzleIds);

            // Record 2nd success
            var stats2 = await service.RecordAttemptAsync("p2", isSuccess: true, timeSpentSeconds: 7);
            Assert.Equal(2, stats2.SolvedCount);
            Assert.Equal(2, stats2.CurrentStreak);
            Assert.True(stats2.CurrentRating > stats1.CurrentRating);

            // Record 3rd success (streak bonus)
            var stats3 = await service.RecordAttemptAsync("p3", isSuccess: true, timeSpentSeconds: 12);
            Assert.Equal(3, stats3.CurrentStreak);
            Assert.Equal(3, stats3.BestStreak);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RecordAttempt_Failure_ResetsStreakAndDecreasesRating()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "puzzles_test_" + Guid.NewGuid().ToString("N"));
        var db = new FakeDatabaseService();
        var profile = new FakeUserProfileService();
        var service = new PuzzleService(db, profile, tempDir);

        try
        {
            // Build a streak first
            await service.RecordAttemptAsync("p1", isSuccess: true, timeSpentSeconds: 5);
            await service.RecordAttemptAsync("p2", isSuccess: true, timeSpentSeconds: 5);

            var preFailStats = await service.GetStatsAsync();
            Assert.Equal(2, preFailStats.CurrentStreak);

            // Fail attempt
            var failedStats = await service.RecordAttemptAsync("p3", isSuccess: false, timeSpentSeconds: 20);
            Assert.Equal(0, failedStats.CurrentStreak);
            Assert.Equal(1, failedStats.FailedCount);
            Assert.True(failedStats.CurrentRating < preFailStats.CurrentRating);
            Assert.Contains("p3", failedStats.FailedPuzzleIds);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExtractPuzzlesFromDatabase_ExtractsAnnotatedBlunder()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "puzzles_test_" + Guid.NewGuid().ToString("N"));
        var db = new FakeDatabaseService();
        var profile = new FakeUserProfileService();
        var service = new PuzzleService(db, profile, tempDir);

        // PGN containing a blunder ($4) and alternative best variation ($1)
        string annotatedPgn = @"[Event ""Online Blitz""]
[Site ""lichess.org""]
[Date ""2026.08.10""]
[White ""jantest""]
[Black ""OpponentGM""]
[Result ""0-1""]
[ECO ""C50""]

1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. O-O Nf6 5. d3 d6 6. c3 a6 7. Re1 O-O 8. h3 h6 9. Nbd2 Be6 10. Bxe6 fxe6 11. Nf1 Nh5 12. Be3 Bxe3 13. fxe3 Qe8 14. d4 Qg6 15. N3d2 Rf7 16. Qe2 Raf8 17. Nh2 Ng3 18. Qg4 $4 (18. Qd3 $1 {Best was Qd3}) 18... Qxg4 0-1";

        db.Headers.Add(new GameHeader
        {
            Id = 101,
            White = "jantest",
            Black = "OpponentGM",
            Date = "2026.08.10",
            Event = "Online Blitz",
            Site = "https://lichess.org/test",
            Eco = "C50",
            Pgn = annotatedPgn
        });

        try
        {
            int extractedCount = await service.ExtractPuzzlesFromDatabaseAsync("My online games");
            Assert.True(extractedCount >= 1);

            var puzzles = await service.GetPuzzlesAsync(new PuzzleFilterOptions { Mode = "blunders" });
            Assert.NotEmpty(puzzles);

            var userPuzzle = puzzles.FirstOrDefault(p => p.GameId == 101);
            Assert.NotNull(userPuzzle);
            Assert.True(userPuzzle.IsUserBlunder);
            Assert.Equal("Qg4", userPuzzle.PlayedBlunderSan);
            Assert.Contains("Qd3", userPuzzle.SolutionMovesSan);
            Assert.False(string.IsNullOrEmpty(userPuzzle.Fen));
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task GetPuzzlesAsync_BlundersMode_OnlyReturnsPuzzlesMatchingUserProfile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "puzzles_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var db = new FakeDatabaseService();
            // Profile is "Jan Testovaci" (Lichess: jantest)
            var profile = new FakeUserProfileService();
            var service = new PuzzleService(db, profile, tempDir);

            // Game where NEITHER player is Jan Testovaci
            string annotatedPgn = @"[Event ""Casual""]
[White ""Alice""]
[Black ""Bob""]
[Result ""1-0""]

1. e4 e5 2. Nf3 Nc6 3. d4 exd4 4. Bc4 Nf6 5. e5 d5 6. Bb5 Ne4 7. Nxd4 Bd7 8. Bxc6 bxc6 9. O-O Bc5 10. Be3 O-O 11. Nd2 $4 (11. f3 $1) 11... Nxd2 1-0";

            db.Headers.Add(new GameHeader
            {
                Id = 999,
                White = "Alice",
                Black = "Bob",
                Pgn = annotatedPgn
            });

            // Extract should skip games where user did not play
            int extracted = await service.ExtractPuzzlesFromDatabaseAsync("My online games");
            Assert.Equal(0, extracted);

            // And even if blunders mode is queried, it returns no blunders
            var blunders = await service.GetPuzzlesAsync(new PuzzleFilterOptions { Mode = "blunders" });
            Assert.Empty(blunders);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExtractPuzzlesFromDatabaseAsync_SkipsUnannotatedGamesInstantly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "puzzles_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var db = new FakeDatabaseService();
            var profile = new FakeUserProfileService();
            var service = new PuzzleService(db, profile, tempDir);

            // Add 100 clean, unannotated games by the user
            for (int i = 0; i < 100; i++)
            {
                db.Headers.Add(new GameHeader
                {
                    Id = i + 1,
                    White = "jantest",
                    Black = "Opponent",
                    Pgn = "1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. O-O Nf6 5. d3 d6 1-0"
                });
            }

            // Extraction should finish in milliseconds and find 0 blunder puzzles without expensive tree parsing
            int extracted = await service.ExtractPuzzlesFromDatabaseAsync("My online games");
            Assert.Equal(0, extracted);

            // Calling GetPuzzlesAsync for blunders should return empty list instantly
            var puzzles = await service.GetPuzzlesAsync(new PuzzleFilterOptions { Mode = "blunders" });
            Assert.Empty(puzzles);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }
}
