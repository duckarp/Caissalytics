using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class UserAnalyticsTests
{
#pragma warning disable CS0067
    private class FakeDatabaseService : IDatabaseService
    {
        public List<GameHeader> Headers { get; set; } = new();
        public event Action? OnActiveDatabaseChanged;
        public event Action? OnReferenceDatabaseChanged;
        public event Action<string>? OnDatabaseModified;

        public Task<List<GameHeader>> GetAllGameHeadersAsync(string? databaseName = null) => Task.FromResult(Headers);
        public Task<List<DatabaseInfo>> GetDatabasesAsync() => Task.FromResult(new List<DatabaseInfo>());
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
        public UserProfile Profile { get; set; } = new();
        public event Action? OnProfileChanged;
        public Task<UserProfile> GetProfileAsync() => Task.FromResult(Profile);
        public Task SaveProfileAsync(UserProfile profile) { Profile = profile; return Task.CompletedTask; }
    }
#pragma warning restore CS0067

    [Fact]
    public void OpeningCatalog_ResolveOpeningName_FromPgnHeader()
    {
        string pgn = @"[Event ""Live Chess""]
[Site ""Chess.com""]
[Date ""2026.07.01""]
[White ""Player1""]
[Black ""Player2""]
[Result ""1-0""]
[ECO ""B30""]
[Opening ""Sicilian Defense: Old Sicilian""]
[Variation ""Open""]

1. e4 c5 2. Nf3 Nc6 1-0";

        string opening = OpeningCatalog.ResolveOpeningName("B30", pgn);
        Assert.Equal("Sicilian Defense: Old Sicilian: Open", opening);
    }

    [Fact]
    public void OpeningCatalog_ResolveOpeningName_FromEcoFallback()
    {
        string openingB30 = OpeningCatalog.ResolveOpeningName("B30", null);
        Assert.Equal("Sicilian Defense: Old Sicilian", openingB30);

        string openingC50 = OpeningCatalog.ResolveOpeningName("C50", "");
        Assert.Equal("Italian Game", openingC50);

        string openingPrefixB = OpeningCatalog.ResolveOpeningName("B99", "");
        Assert.Equal("Sicilian Defense: Najdorf, Main Line", openingPrefixB);

        string openingPrefixD = OpeningCatalog.ResolveOpeningName("D02", "");
        Assert.Equal("Queen's Pawn: London System", openingPrefixD);

        string unknown = OpeningCatalog.ResolveOpeningName("Z99", "");
        Assert.Equal("Opening (Z99)", unknown);
    }

    [Fact]
    public async Task UserAnalyticsService_CalculatesMetricsFromUserPerspective()
    {
        var fakeDb = new FakeDatabaseService();
        var fakeProfile = new FakeUserProfileService
        {
            Profile = new UserProfile
            {
                FirstName = "Tomas",
                LastName = "Kovac",
                LichessUsername = "duckarp",
                ChessComUsername = "duckarp"
            }
        };

        // 4 sample games:
        // Game 1: User as White -> Won (1-0)
        // Game 2: User as Black -> Won (0-1)
        // Game 3: User as White -> Lost (0-1)
        // Game 4: User as Black -> Drew (1/2-1/2)
        fakeDb.Headers = new List<GameHeader>
        {
            new() { Id = 1, White = "duckarp", Black = "Opponent1", WhiteElo = 1800, BlackElo = 1750, Result = "1-0", Date = "2026.07.01", Eco = "B30", Pgn = "" },
            new() { Id = 2, White = "Opponent2", Black = "duckarp", WhiteElo = 1820, BlackElo = 1810, Result = "0-1", Date = "2026.07.02", Eco = "C50", Pgn = "" },
            new() { Id = 3, White = "duckarp", Black = "Opponent3", WhiteElo = 1830, BlackElo = 1900, Result = "0-1", Date = "2026.07.03", Eco = "B30", Pgn = "" },
            new() { Id = 4, White = "Opponent4", Black = "duckarp", WhiteElo = 1800, BlackElo = 1820, Result = "1/2-1/2", Date = "2026.07.04", Eco = "C50", Pgn = "" },
        };

        var analyticsService = new UserAnalyticsService(fakeDb, fakeProfile);
        var report = await analyticsService.GenerateAnalyticsReportAsync("My online games", fakeProfile.Profile);

        // Total games: 4
        // Wins: 2 (Game 1, Game 2)
        // Losses: 1 (Game 3)
        // Draws: 1 (Game 4)
        Assert.Equal(4, report.TotalGames);
        Assert.Equal(2, report.Wins);
        Assert.Equal(1, report.Losses);
        Assert.Equal(1, report.Draws);

        // WinRate = 50.0%, ScoreRate = (2 + 0.5*1)/4 = 62.5%
        Assert.Equal(50.0, report.WinRate, 1);
        Assert.Equal(62.5, report.ScoreRate, 1);

        // White Stats:
        // Total: 2 (Game 1, Game 3)
        // 1 Win, 1 Loss, 0 Draws -> 50% win rate
        Assert.Equal(2, report.WhiteStats.TotalGames);
        Assert.Equal(1, report.WhiteStats.Wins);
        Assert.Equal(1, report.WhiteStats.Losses);
        Assert.Equal(0, report.WhiteStats.Draws);
        Assert.Equal(50.0, report.WhiteStats.WinRate, 1);

        // Black Stats:
        // Total: 2 (Game 2, Game 4)
        // 1 Win, 0 Losses, 1 Draw -> 50% win rate, 75% score rate
        Assert.Equal(2, report.BlackStats.TotalGames);
        Assert.Equal(1, report.BlackStats.Wins);
        Assert.Equal(0, report.BlackStats.Losses);
        Assert.Equal(1, report.BlackStats.Draws);
        Assert.Equal(75.0, report.BlackStats.ScoreRate, 1);

        // Rating Progression:
        // Ratings: 1800, 1810, 1830, 1820
        Assert.Equal(4, report.RatingHistory.Count);
        Assert.Equal(1830, report.PeakRating);
        Assert.Equal(1800, report.LowestRating);
        Assert.Equal(1820, report.CurrentRating);

        // Openings:
        // B30: 2 games (1 Win, 1 Loss)
        // C50: 2 games (1 Win, 1 Draw)
        Assert.Equal(2, report.TopOpenings.Count);
        var b30 = report.TopOpenings.First(o => o.Eco == "B30");
        Assert.Equal(2, b30.TotalGames);
        Assert.Equal(1, b30.Wins);
        Assert.Equal(1, b30.Losses);

        var c50 = report.TopOpenings.First(o => o.Eco == "C50");
        Assert.Equal(2, c50.TotalGames);
        Assert.Equal(1, c50.Wins);
        Assert.Equal(1, c50.Draws);
        Assert.Equal(75.0, c50.ScoreRate, 1);
    }

    [Fact]
    public async Task UserAnalyticsService_CalculatesStreaksAccurately()
    {
        var fakeDb = new FakeDatabaseService();
        var fakeProfile = new FakeUserProfileService
        {
            Profile = new UserProfile { LichessUsername = "kasparov" }
        };

        // Chronological: 3 wins, 1 loss, 2 wins
        fakeDb.Headers = new List<GameHeader>
        {
            new() { Id = 1, White = "kasparov", Black = "P1", Result = "1-0", Date = "2026.01.01" },
            new() { Id = 2, White = "kasparov", Black = "P2", Result = "1-0", Date = "2026.01.02" },
            new() { Id = 3, White = "kasparov", Black = "P3", Result = "1-0", Date = "2026.01.03" },
            new() { Id = 4, White = "kasparov", Black = "P4", Result = "0-1", Date = "2026.01.04" },
            new() { Id = 5, White = "kasparov", Black = "P5", Result = "1-0", Date = "2026.01.05" },
            new() { Id = 6, White = "kasparov", Black = "P6", Result = "1-0", Date = "2026.01.06" }
        };

        var analyticsService = new UserAnalyticsService(fakeDb, fakeProfile);
        var report = await analyticsService.GenerateAnalyticsReportAsync("ALL", fakeProfile.Profile);

        // Best streak: 3 wins (games 1-3)
        Assert.Equal(3, report.BestWinStreak);

        // Current streak: +2 Wins (games 5-6)
        Assert.Equal("+2 Wins", report.CurrentStreak);
    }

    [Fact]
    public async Task UserAnalyticsService_EmptyGames_ReturnsEmptyReportWithoutErrors()
    {
        var fakeDb = new FakeDatabaseService { Headers = new List<GameHeader>() };
        var fakeProfile = new FakeUserProfileService
        {
            Profile = new UserProfile { LichessUsername = "empty_player" }
        };

        var analyticsService = new UserAnalyticsService(fakeDb, fakeProfile);
        var report = await analyticsService.GenerateAnalyticsReportAsync("Default", fakeProfile.Profile);

        Assert.NotNull(report);
        Assert.Equal(0, report.TotalGames);
        Assert.Equal(0, report.Wins);
        Assert.Equal(0.0, report.WinRate);
        Assert.Empty(report.RatingHistory);
        Assert.Empty(report.TopOpenings);
        Assert.Empty(report.RecentGames);
    }

    [Fact]
    public async Task UserAnalyticsService_SeparatesPlatformRatingsAccurately()
    {
        var fakeDb = new FakeDatabaseService();
        var fakeProfile = new FakeUserProfileService
        {
            Profile = new UserProfile
            {
                LichessUsername = "duckarp",
                ChessComUsername = "duckarp"
            }
        };

        fakeDb.Headers = new List<GameHeader>
        {
            // 2 Lichess games
            new() { Id = 1, Site = "https://lichess.org/abc", White = "duckarp", Black = "Opp1", WhiteElo = 2000, Result = "1-0", Date = "2026.01.01" },
            new() { Id = 2, Site = "https://lichess.org/def", White = "Opp2", Black = "duckarp", BlackElo = 2062, Result = "1-0", Date = "2026.01.02" },

            // 2 Chess.com games
            new() { Id = 3, Site = "Chess.com", White = "duckarp", Black = "Opp3", WhiteElo = 1750, Result = "1-0", Date = "2026.02.01" },
            new() { Id = 4, Site = "Chess.com", White = "Opp4", Black = "duckarp", BlackElo = 1825, Result = "0-1", Date = "2026.02.02" }
        };

        var analyticsService = new UserAnalyticsService(fakeDb, fakeProfile);
        var report = await analyticsService.GenerateAnalyticsReportAsync("My online games", fakeProfile.Profile);

        Assert.Equal(4, report.TotalGames);
        Assert.Equal(2, report.PlatformRatings.Count);

        var lichess = report.PlatformRatings.FirstOrDefault(p => p.PlatformId == "lichess");
        Assert.NotNull(lichess);
        Assert.Equal("Lichess", lichess.PlatformName);
        Assert.Equal(2062, lichess.PeakRating);
        Assert.Equal(2062, lichess.CurrentRating);
        Assert.Equal(2, lichess.Points.Count);

        var chesscom = report.PlatformRatings.FirstOrDefault(p => p.PlatformId == "chesscom");
        Assert.NotNull(chesscom);
        Assert.Equal("Chess.com", chesscom.PlatformName);
        Assert.Equal(1825, chesscom.PeakRating);
        Assert.Equal(1825, chesscom.CurrentRating);
        Assert.Equal(2, chesscom.Points.Count);

        // Test PlatformFilter: Filtering to lichess only
        var lichessReport = await analyticsService.GenerateAnalyticsReportAsync("My online games", fakeProfile.Profile, new AnalyticsFilterOptions { PlatformFilter = "lichess" });
        Assert.Equal(2, lichessReport.TotalGames);
        Assert.Single(lichessReport.PlatformRatings);
        Assert.Equal("lichess", lichessReport.PlatformRatings[0].PlatformId);
    }
}
