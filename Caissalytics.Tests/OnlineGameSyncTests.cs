using System.Net;
using Caissalytics.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Caissalytics.Tests;

public class OnlineGameSyncTests : IDisposable
{
    private readonly string _testStorageDir;
    private readonly string _testConfigFile;
    private readonly DatabaseManager _dbManager;

    public OnlineGameSyncTests()
    {
        _testStorageDir = Path.Combine(Path.GetTempPath(), "Caissalytics_SyncTests_" + Guid.NewGuid().ToString("N"));
        _testConfigFile = Path.Combine(_testStorageDir, "online_sync_config.json");
        Directory.CreateDirectory(_testStorageDir);
        _dbManager = new DatabaseManager(_testStorageDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testStorageDir))
            {
                Directory.Delete(_testStorageDir, true);
            }
        }
        catch
        {
            // Ignore cleanup lock issues on temp dirs
        }
    }

    [Fact]
    public async Task OnlineSyncConfig_SaveAndLoad_PersistsCorrectly()
    {
        var mockFactory = new DummyHttpClientFactory();
        var service = new OnlineGameSyncService(_dbManager, mockFactory, _testConfigFile);

        var config = new OnlineSyncConfig
        {
            LichessUsername = "TestLichessUser",
            ChessComUsername = "TestChessComUser",
            MaxGamesPerPlatform = 250,
            LastSyncUtc = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc)
        };

        await service.SaveConfigAsync(config);

        var loaded = await service.GetConfigAsync();
        Assert.Equal("TestLichessUser", loaded.LichessUsername);
        Assert.Equal("TestChessComUser", loaded.ChessComUsername);
        Assert.Equal(250, loaded.MaxGamesPerPlatform);
        Assert.NotNull(loaded.LastSyncUtc);
    }

    [Fact]
    public async Task Deduplication_SkipsDuplicateOnlineGamesByUrl()
    {
        string dbName = "DeduplicationTestDb";
        await _dbManager.CreateDatabaseAsync(dbName);

        string pgn = @"
[Event ""Lichess Blitz""]
[Site ""https://lichess.org/game12345""]
[Date ""2026.09.01""]
[White ""PlayerOne""]
[Black ""PlayerTwo""]
[Result ""1-0""]
[ECO ""C50""]

1. e4 e5 2. Nf3 Nc6 3. Bc4 1-0

[Event ""Chess.com Live""]
[Site ""Chess.com""]
[Link ""https://www.chess.com/game/live/game99999""]
[Date ""2026.09.02""]
[White ""PlayerThree""]
[Black ""PlayerFour""]
[Result ""0-1""]
[ECO ""B01""]

1. e4 d5 2. exd5 Qxd5 0-1
";

        var progressList1 = new List<PgnImportProgress>();
        var progress1 = new Progress<PgnImportProgress>(p => progressList1.Add(p));

        // First import: both should be saved
        await _dbManager.ImportPgnTextAsync(dbName, pgn, progress1, deduplicate: true);

        var (games1, count1) = await _dbManager.SearchGamesAsync(dbName, new GameFilter());
        Assert.Equal(2, count1);

        // Check canonical URLs were stored in site column
        var lichessGame = games1.FirstOrDefault(g => g.Site.Contains("lichess.org"));
        Assert.NotNull(lichessGame);
        Assert.Equal("https://lichess.org/game12345", lichessGame.Site);

        var chesscomGame = games1.FirstOrDefault(g => g.Site.Contains("chess.com/game/live"));
        Assert.NotNull(chesscomGame);
        Assert.Equal("https://www.chess.com/game/live/game99999", chesscomGame.Site);

        // Second import of the EXACT same PGN with deduplicate = true
        var progressList2 = new List<PgnImportProgress>();
        var progress2 = new DirectProgress<PgnImportProgress>(p => progressList2.Add(p));

        await _dbManager.ImportPgnTextAsync(dbName, pgn, progress2, deduplicate: true);

        var (games2, count2) = await _dbManager.SearchGamesAsync(dbName, new GameFilter());
        // Count must still be 2! No duplicates!
        Assert.Equal(2, count2);

        var finalProgress = progressList2.LastOrDefault();
        Assert.NotNull(finalProgress);
        Assert.Equal(2, finalProgress.GamesSkipped);
        Assert.Equal(0, finalProgress.GamesSaved);
    }

    [Fact]
    public async Task SyncGamesAsync_EmptyUsernames_ReturnsValidationError()
    {
        var mockFactory = new DummyHttpClientFactory();
        var service = new OnlineGameSyncService(_dbManager, mockFactory, _testConfigFile);

        var emptyConfig = new OnlineSyncConfig
        {
            LichessUsername = "   ",
            ChessComUsername = ""
        };

        var result = await service.SyncGamesAsync(emptyConfig);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("specify at least one username"));
    }

    [Fact]
    public async Task SyncGamesAsync_CreatesOnlineGamesDatabaseIfMissing()
    {
        var mockFactory = new DummyHttpClientFactory();
        var service = new OnlineGameSyncService(_dbManager, mockFactory, _testConfigFile);

        // Verify "My online games" does not exist yet
        var dbsBefore = await _dbManager.GetDatabasesAsync();
        Assert.DoesNotContain(dbsBefore, d => d.Name == service.OnlineGamesDatabaseName);

        // Run sync with non-existent user on mock handler (will fail network or user not found, but DB must be created)
        var config = new OnlineSyncConfig
        {
            LichessUsername = "dummy_test_user_xyz",
            MaxGamesPerPlatform = 50
        };

        await service.SyncGamesAsync(config);

        var dbsAfter = await _dbManager.GetDatabasesAsync();
        Assert.Contains(dbsAfter, d => d.Name == service.OnlineGamesDatabaseName);
    }

    [Fact]
    public void GameHeader_PlatformDetection_IdentifiesPlatformsCorrectly()
    {
        var lichessGame = new GameHeader
        {
            Site = "https://lichess.org/aBcDeFgH",
            White = "player1",
            Black = "player2"
        };
        Assert.Equal("lichess", lichessGame.Platform);
        Assert.Equal("Lichess", lichessGame.PlatformName);
        Assert.Equal("https://lichess.org/aBcDeFgH", lichessGame.ExternalUrl);

        var chesscomGame = new GameHeader
        {
            Site = "https://www.chess.com/game/live/123456789",
            White = "player1",
            Black = "player2"
        };
        Assert.Equal("chesscom", chesscomGame.Platform);
        Assert.Equal("Chess.com", chesscomGame.PlatformName);
        Assert.Equal("https://www.chess.com/game/live/123456789", chesscomGame.ExternalUrl);

        var chesscomSiteTag = new GameHeader
        {
            Site = "Chess.com",
            Event = "Live Chess"
        };
        Assert.Equal("chesscom", chesscomSiteTag.Platform);
        Assert.Equal("Chess.com", chesscomSiteTag.PlatformName);
        Assert.Null(chesscomSiteTag.ExternalUrl);

        var otbGame = new GameHeader
        {
            Site = "London ENG",
            Event = "World Championship",
            White = "Kasparov, G.",
            Black = "Karpov, A."
        };
        Assert.Equal("", otbGame.Platform);
        Assert.Equal("", otbGame.PlatformName);
        Assert.Null(otbGame.ExternalUrl);
    }

    [Fact]
    public async Task OnlineGamesDatabase_NameMatches_AndRecreatesAfterDeletion()
    {
        var mockFactory = new DummyHttpClientFactory();
        var service = new OnlineGameSyncService(_dbManager, mockFactory, _testConfigFile);

        Assert.Equal("My online games", service.OnlineGamesDatabaseName);

        // Create database
        var db = await _dbManager.CreateDatabaseAsync(service.OnlineGamesDatabaseName);
        Assert.True(File.Exists(db.FilePath));

        var dbsBefore = await _dbManager.GetDatabasesAsync();
        Assert.Contains(dbsBefore, d => d.Name == service.OnlineGamesDatabaseName);

        // Delete database
        bool deleted = await _dbManager.DeleteDatabaseAsync(service.OnlineGamesDatabaseName);
        Assert.True(deleted);

        var dbsAfter = await _dbManager.GetDatabasesAsync();
        Assert.DoesNotContain(dbsAfter, d => d.Name == service.OnlineGamesDatabaseName);

        // Recreate cleanly
        var recreated = await _dbManager.CreateDatabaseAsync(service.OnlineGamesDatabaseName);
        Assert.True(File.Exists(recreated.FilePath));
    }

    [Fact]
    public async Task PurgeNonSyncedGamesAsync_RemovesForeignGames_AndKeepsUserGames()
    {
        var mockFactory = new DummyHttpClientFactory();
        var service = new OnlineGameSyncService(_dbManager, mockFactory, _testConfigFile);

        var profile = new UserProfile
        {
            FirstName = "Tomas",
            LastName = "K",
            LichessUsername = "tomask_lic",
            ChessComUsername = "tomask_cc"
        };

        // 1. Legitimate user Lichess game
        string userGameLichess = @"[Event ""Rated Blitz game""]
[Site ""https://lichess.org/abc12345""]
[White ""tomask_lic""]
[Black ""RandomOpponent""]
[Result ""1-0""]

1. e4 e5 1-0";

        // 2. Legitimate user Chess.com game
        string userGameChessCom = @"[Event ""Live Chess""]
[Site ""Chess.com""]
[White ""GrandmasterX""]
[Black ""tomask_cc""]
[Result ""0-1""]

1. d4 Nf6 0-1";

        // 3. Foreign / scouted game accidentally imported (neither player is user, site is OTB)
        string foreignGame1 = @"[Event ""Chess-Results scouting""]
[Site ""chess-results.com""]
[White ""OpponentGrandmaster""]
[Black ""OtherPlayer""]
[Result ""1-0""]

1. c4 c5 1-0";

        // 4. Foreign game with no online platform
        string foreignGame2 = @"[Event ""Casual Club""]
[Site ""Local Club""]
[White ""PlayerA""]
[Black ""PlayerB""]
[Result ""1/2-1/2""]

1. e4 e5 1/2-1/2";

        await _dbManager.ImportPgnTextAsync(service.OnlineGamesDatabaseName, userGameLichess, allowProtectedDatabase: true);
        await _dbManager.ImportPgnTextAsync(service.OnlineGamesDatabaseName, userGameChessCom, allowProtectedDatabase: true);
        await _dbManager.ImportPgnTextAsync(service.OnlineGamesDatabaseName, foreignGame1, allowProtectedDatabase: true);
        await _dbManager.ImportPgnTextAsync(service.OnlineGamesDatabaseName, foreignGame2, allowProtectedDatabase: true);

        var (allBefore, countBefore) = await _dbManager.SearchGamesAsync(service.OnlineGamesDatabaseName, new GameFilter());
        Assert.Equal(4, countBefore);

        // Run purge
        int purged = await service.PurgeNonSyncedGamesAsync(profile);
        Assert.Equal(2, purged);

        var (allAfter, countAfter) = await _dbManager.SearchGamesAsync(service.OnlineGamesDatabaseName, new GameFilter());
        Assert.Equal(2, countAfter);
        Assert.Contains(allAfter, g => g.White == "tomask_lic");
        Assert.Contains(allAfter, g => g.Black == "tomask_cc");
        Assert.DoesNotContain(allAfter, g => g.White == "OpponentGrandmaster");
        Assert.DoesNotContain(allAfter, g => g.White == "PlayerA");
    }

    private class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var handler = new MockHttpMessageHandler();
            return new HttpClient(handler);
        }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Return 404 for test dummy user
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                ReasonPhrase = "Not Found"
            });
        }
    }

    private class DirectProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public DirectProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
