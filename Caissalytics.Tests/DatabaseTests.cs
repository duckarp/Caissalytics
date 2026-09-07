using Caissalytics.Core;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _testDir;
    private readonly DatabaseManager _dbManager;

    public DatabaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"caissalytics_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _dbManager = new DatabaseManager(_testDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors on temp dir
        }
    }

    [Fact]
    public async Task DatabaseCreationAndListing_Works()
    {
        var db1 = await _dbManager.CreateDatabaseAsync("Masters");
        var db2 = await _dbManager.CreateDatabaseAsync("Sicilians");

        var databases = await _dbManager.GetDatabasesAsync();

        Assert.Contains(databases, d => d.Name == "Masters");
        Assert.Contains(databases, d => d.Name == "Sicilians");
        Assert.True(File.Exists(db1.FilePath));
        Assert.True(File.Exists(db2.FilePath));
    }

    [Fact]
    public async Task StreamingPgnImporter_ParsesAndSavesGamesAndPositions()
    {
        await _dbManager.CreateDatabaseAsync("TestPgn");

        string testPgn = @"
[Event ""London 1851""]
[Site ""London""]
[Date ""1851.06.21""]
[White ""Anderssen, Adolf""]
[Black ""Kieseritzky, Lionel""]
[Result ""1-0""]
[ECO ""C33""]

1. e4 e5 2. f4 exf4 3. Bc4 Qh4+ 4. Kf1 b5 1-0

[Event ""Opera House""]
[Site ""Paris""]
[Date ""1858.??.??""]
[White ""Morphy, Paul""]
[Black ""Duke Karl""]
[Result ""1-0""]
[ECO ""C41""]

1. e4 e5 2. Nf3 d6 3. d4 Bg4 1-0

[Event ""Casual Draw""]
[Site ""Online""]
[Date ""2024.01.01""]
[White ""PlayerA""]
[Black ""PlayerB""]
[Result ""1/2-1/2""]
[ECO ""B00""]

1. e4 c5 2. Nf3 d6 1/2-1/2
";

        await _dbManager.ImportPgnTextAsync("TestPgn", testPgn);

        var (games, count) = await _dbManager.SearchGamesAsync("TestPgn", new GameFilter());
        Assert.Equal(3, count);
        Assert.Equal(3, games.Count);

        var anderssen = games.FirstOrDefault(g => g.White.Contains("Anderssen"));
        Assert.NotNull(anderssen);
        Assert.Equal("1-0", anderssen.Result);
        Assert.Equal("C33", anderssen.Eco);
    }

    [Fact]
    public async Task ImportPgnTextAsync_PastedPgnWithVariationsAndComments_ImportsCorrectly()
    {
        await _dbManager.CreateDatabaseAsync("PastedPgnDb");

        string pastedPgn = @"
[Event ""World Championship 1972""]
[Site ""Reykjavik ISL""]
[Date ""1972.07.23""]
[Round ""6""]
[White ""Fischer, Robert J.""]
[Black ""Spassky, Boris V.""]
[Result ""1-0""]
[WhiteElo ""2785""]
[BlackElo ""2660""]
[ECO ""D59""]

1. c4 e6 2. Nf3 d5 3. d4 Nf6 4. Nc3 Be7 5. Bg5 O-O 6. e3 h6 7. Bh4 b6 8. cxd5 Nxd5 9. Bxe7 Qxe7 10. Nxd5 exd5 11. Rc1 Be6 12. Qa4 c5 13. Qa3 Rc8 1-0
";

        await _dbManager.ImportPgnTextAsync("PastedPgnDb", pastedPgn);

        var (games, count) = await _dbManager.SearchGamesAsync("PastedPgnDb", new GameFilter());
        Assert.Equal(1, count);
        Assert.Single(games);

        var game = games[0];
        Assert.Equal("Fischer, Robert J.", game.White);
        Assert.Equal("Spassky, Boris V.", game.Black);
        Assert.Equal(2785, game.WhiteElo);
        Assert.Equal(2660, game.BlackElo);
        Assert.Equal("1-0", game.Result);
        Assert.Equal("D59", game.Eco);
    }

    [Fact]
    public async Task QueryPosition_ZobristLookup_ComputesAccurateFrequenciesAndWinRates()
    {
        await _dbManager.CreateDatabaseAsync("OpeningRefTest");

        // 3 games:
        // Game 1: 1. e4 e5 (1-0)
        // Game 2: 1. e4 c5 (1/2-1/2)
        // Game 3: 1. d4 d5 (0-1)
        string pgn = @"
[Event ""Game 1""]
[White ""W1""]
[Black ""B1""]
[Result ""1-0""]

1. e4 e5 1-0

[Event ""Game 2""]
[White ""W2""]
[Black ""B2""]
[Result ""1/2-1/2""]

1. e4 c5 1/2-1/2

[Event ""Game 3""]
[White ""W3""]
[Black ""B3""]
[Result ""0-1""]

1. d4 d5 0-1
";

        await _dbManager.ImportPgnTextAsync("OpeningRefTest", pgn);

        // Query starting position
        var startPos = FenParser.Parse(BoardPosition.StartFen);
        var refResult = await _dbManager.QueryPositionAsync("OpeningRefTest", startPos.ZobristKey);

        Assert.Equal(3, refResult.TotalPositionGames);
        Assert.Equal(2, refResult.CandidateMoves.Count); // e4 and d4

        var e4Move = refResult.CandidateMoves.FirstOrDefault(m => m.MoveSan == "e4");
        Assert.NotNull(e4Move);
        Assert.Equal(2, e4Move.TotalGames);
        Assert.Equal(1, e4Move.WhiteWins);
        Assert.Equal(1, e4Move.Draws);
        Assert.Equal(0, e4Move.BlackWins);
        Assert.Equal(50.0, e4Move.WhiteWinRate);
        Assert.Equal(50.0, e4Move.DrawRate);
        Assert.Equal(75.0, e4Move.ScoreRate); // 1 win + 0.5 draw = 1.5 / 2 = 75%

        var d4Move = refResult.CandidateMoves.FirstOrDefault(m => m.MoveSan == "d4");
        Assert.NotNull(d4Move);
        Assert.Equal(1, d4Move.TotalGames);
        Assert.Equal(0, d4Move.WhiteWins);
        Assert.Equal(0, d4Move.Draws);
        Assert.Equal(1, d4Move.BlackWins);
        Assert.Equal(100.0, d4Move.BlackWinRate);

        // Query position after 1. e4
        var posAfterE4 = MoveGenerator.ApplyMove(startPos, SanParser.ParseSan(startPos, "e4"));
        var refE4 = await _dbManager.QueryPositionAsync("OpeningRefTest", posAfterE4.ZobristKey);

        Assert.Equal(2, refE4.TotalPositionGames);
        Assert.Equal(2, refE4.CandidateMoves.Count); // e5 and c5
        Assert.Contains(refE4.CandidateMoves, m => m.MoveSan == "e5");
        Assert.Contains(refE4.CandidateMoves, m => m.MoveSan == "c5");
    }

    [Fact]
    public async Task MultiDatabase_Isolation_And_Switching_Works()
    {
        await _dbManager.CreateDatabaseAsync("DatabaseA");
        await _dbManager.CreateDatabaseAsync("DatabaseB");

        string pgnA = @"
[Event ""Tournament A""]
[White ""Kasparov, Garry""]
[Black ""Karpov, Anatoly""]
[Result ""1-0""]

1. e4 c5 1-0
";

        string pgnB = @"
[Event ""Tournament B""]
[White ""Carlsen, Magnus""]
[Black ""Nakamura, Hikaru""]
[Result ""0-1""]

1. d4 Nf6 0-1
";

        await _dbManager.ImportPgnTextAsync("DatabaseA", pgnA);
        await _dbManager.ImportPgnTextAsync("DatabaseB", pgnB);

        var (gamesA, countA) = await _dbManager.SearchGamesAsync("DatabaseA", new GameFilter());
        var (gamesB, countB) = await _dbManager.SearchGamesAsync("DatabaseB", new GameFilter());

        Assert.Equal(1, countA);
        Assert.Equal("Kasparov, Garry", gamesA[0].White);

        Assert.Equal(1, countB);
        Assert.Equal("Carlsen, Magnus", gamesB[0].White);

        // Position lookup isolation
        var startPos = FenParser.Parse(BoardPosition.StartFen);
        var refA = await _dbManager.QueryPositionAsync("DatabaseA", startPos.ZobristKey);
        var refB = await _dbManager.QueryPositionAsync("DatabaseB", startPos.ZobristKey);

        Assert.Single(refA.CandidateMoves);
        Assert.Equal("e4", refA.CandidateMoves[0].MoveSan);

        Assert.Single(refB.CandidateMoves);
        Assert.Equal("d4", refB.CandidateMoves[0].MoveSan);
    }

    [Fact]
    public async Task SearchGames_FilteringByPlayerEcoAndResult_Works()
    {
        await _dbManager.CreateDatabaseAsync("SearchTest");

        string pgn = @"
[Event ""World Championship""]
[White ""Kasparov, Garry""]
[Black ""Anand, Viswanathan""]
[Result ""1-0""]
[ECO ""B80""]
[Date ""1995.10.10""]

1. e4 c5 2. Nf3 d6 1-0

[Event ""World Championship""]
[White ""Anand, Viswanathan""]
[Black ""Kasparov, Garry""]
[Result ""0-1""]
[ECO ""C80""]
[Date ""1995.10.12""]

1. e4 e5 2. Nf3 Nc6 0-1

[Event ""Match""]
[White ""Fischer, Robert James""]
[Black ""Spassky, Boris""]
[Result ""1/2-1/2""]
[ECO ""B88""]
[Date ""1972.08.15""]

1. e4 c5 2. Nf3 d6 1/2-1/2
";

        await _dbManager.ImportPgnTextAsync("SearchTest", pgn);

        // 1. Filter by Player
        var (kasparovGames, kasparovCount) = await _dbManager.SearchGamesAsync("SearchTest", new GameFilter { Player = "Kasparov" });
        Assert.Equal(2, kasparovCount);

        // 2. Filter by ECO
        var (b80Games, b80Count) = await _dbManager.SearchGamesAsync("SearchTest", new GameFilter { Eco = "B8" });
        Assert.Equal(2, b80Count); // B80 and B88

        // 3. Filter by Result
        var (drawGames, drawCount) = await _dbManager.SearchGamesAsync("SearchTest", new GameFilter { Result = "1/2-1/2" });
        Assert.Equal(1, drawCount);
        Assert.Equal("Fischer, Robert James", drawGames[0].White);
    }

    [Fact]
    public async Task DeleteDatabase_RemovesFiles_AndFallsBackToAnother()
    {
        await _dbManager.CreateDatabaseAsync("ToKeep");
        var toDelete = await _dbManager.CreateDatabaseAsync("ToDelete");

        Assert.True(File.Exists(toDelete.FilePath));

        bool deleted = await _dbManager.DeleteDatabaseAsync("ToDelete");
        Assert.True(deleted);
        Assert.False(File.Exists(toDelete.FilePath));

        var dbs = await _dbManager.GetDatabasesAsync();
        Assert.DoesNotContain(dbs, d => d.Name == "ToDelete");
        Assert.Contains(dbs, d => d.Name == "ToKeep");
    }

    [Fact]
    public async Task SaveGame_And_UpdateGame_PersistsDataAndIndexesPositions_Works()
    {
        await _dbManager.CreateDatabaseAsync("SaveGameTest");

        var newGame = new GameHeader
        {
            White = "Nakamura, Hikaru",
            Black = "Carlsen, Magnus",
            WhiteElo = 2880,
            BlackElo = 2885,
            Result = "1-0",
            Date = "2024.05.20",
            Event = "Speed Chess Championship",
            Eco = "B01",
            Pgn = @"[Event ""Speed Chess Championship""]
[White ""Nakamura, Hikaru""]
[Black ""Carlsen, Magnus""]
[Result ""1-0""]
[Date ""2024.05.20""]
[ECO ""B01""]

1. e4 d5 2. exd5 Qxd5 3. Nc3 Qa5 1-0"
        };

        long gameId = await _dbManager.SaveGameAsync("SaveGameTest", newGame);
        Assert.True(gameId > 0);

        // Verify game can be retrieved
        var retrieved = await _dbManager.GetGameByIdAsync("SaveGameTest", gameId);
        Assert.NotNull(retrieved);
        Assert.Equal("Nakamura, Hikaru", retrieved.White);
        Assert.Equal("Carlsen, Magnus", retrieved.Black);
        Assert.Equal("1-0", retrieved.Result);
        Assert.Equal(6, retrieved.PlyCount); // 6 plies

        // Verify position lookup reflects the saved game
        var startPos = FenParser.Parse(BoardPosition.StartFen);
        var refStart = await _dbManager.QueryPositionAsync("SaveGameTest", startPos.ZobristKey);
        Assert.Single(refStart.CandidateMoves);
        Assert.Equal("e4", refStart.CandidateMoves[0].MoveSan);
        Assert.Equal(1, refStart.CandidateMoves[0].TotalGames);
        Assert.Equal(1, refStart.CandidateMoves[0].WhiteWins);

        // Update existing game (e.g. changing result to 1/2-1/2 and editing date)
        retrieved.Result = "1/2-1/2";
        retrieved.Date = "2024.05.21";
        retrieved.Pgn = @"[Event ""Speed Chess Championship""]
[White ""Nakamura, Hikaru""]
[Black ""Carlsen, Magnus""]
[Result ""1/2-1/2""]
[Date ""2024.05.21""]

1. e4 d5 2. exd5 Qxd5 1/2-1/2";

        long updatedId = await _dbManager.SaveGameAsync("SaveGameTest", retrieved);
        Assert.Equal(gameId, updatedId);

        var updated = await _dbManager.GetGameByIdAsync("SaveGameTest", gameId);
        Assert.NotNull(updated);
        Assert.Equal("1/2-1/2", updated.Result);
        Assert.Equal("2024.05.21", updated.Date);
        Assert.Equal(4, updated.PlyCount);

        // Position lookup now reflects draw
        var refUpdated = await _dbManager.QueryPositionAsync("SaveGameTest", startPos.ZobristKey);
        Assert.Equal(1, refUpdated.CandidateMoves[0].Draws);
        Assert.Equal(0, refUpdated.CandidateMoves[0].WhiteWins);
    }

    [Fact]
    public async Task DeleteGame_RemovesGameAndCascadeDeletesPositions_Works()
    {
        await _dbManager.CreateDatabaseAsync("DeleteGameTest");

        var game = new GameHeader
        {
            White = "Tal, Mikhail",
            Black = "Botvinnik, Mikhail",
            Result = "1-0",
            Date = "1960.03.15",
            Pgn = "1. e4 c5 2. Nf3 d6 1-0"
        };

        long gameId = await _dbManager.SaveGameAsync("DeleteGameTest", game);
        Assert.True(gameId > 0);

        var (gamesBefore, countBefore) = await _dbManager.SearchGamesAsync("DeleteGameTest", new GameFilter());
        Assert.Equal(1, countBefore);

        var startPos = FenParser.Parse(BoardPosition.StartFen);
        var refBefore = await _dbManager.QueryPositionAsync("DeleteGameTest", startPos.ZobristKey);
        Assert.Equal(1, refBefore.TotalPositionGames);

        // Delete game
        bool deleted = await _dbManager.DeleteGameAsync("DeleteGameTest", gameId);
        Assert.True(deleted);

        // Verify game is gone
        var (gamesAfter, countAfter) = await _dbManager.SearchGamesAsync("DeleteGameTest", new GameFilter());
        Assert.Equal(0, countAfter);
        Assert.Empty(gamesAfter);

        // Verify positions are purged
        var refAfter = await _dbManager.QueryPositionAsync("DeleteGameTest", startPos.ZobristKey);
        Assert.Equal(0, refAfter.TotalPositionGames);
        Assert.Empty(refAfter.CandidateMoves);
    }

    [Fact]
    public async Task SaveGame_CanRewriteOrSaveAsNew_FiresOnDatabaseModified()
    {
        await _dbManager.CreateDatabaseAsync("RewriteTest");

        string? modifiedDb = null;
        _dbManager.OnDatabaseModified += db => modifiedDb = db;

        var originalGame = new GameHeader
        {
            White = "Smyslov, Vasily",
            Black = "Geller, Efim",
            Result = "1-0",
            Date = "1955.02.10",
            Pgn = "1. e4 c5 2. Nf3 Nc6 1-0"
        };

        long gameId1 = await _dbManager.SaveGameAsync("RewriteTest", originalGame);
        Assert.Equal("RewriteTest", modifiedDb);
        Assert.True(gameId1 > 0);

        var (games1, count1) = await _dbManager.SearchGamesAsync("RewriteTest", new GameFilter());
        Assert.Equal(1, count1);

        // 1. Rewrite existing record (Id = gameId1)
        modifiedDb = null;
        var rewriteHeader = new GameHeader
        {
            Id = gameId1,
            White = "Smyslov, Vasily",
            Black = "Geller, Efim",
            Result = "1/2-1/2",
            Date = "1955.02.10",
            Pgn = "1. e4 c5 2. Nf3 Nc6 1/2-1/2"
        };

        long rewrittenId = await _dbManager.SaveGameAsync("RewriteTest", rewriteHeader);
        Assert.Equal(gameId1, rewrittenId);
        Assert.Equal("RewriteTest", modifiedDb);

        var (gamesRewritten, countRewritten) = await _dbManager.SearchGamesAsync("RewriteTest", new GameFilter());
        Assert.Equal(1, countRewritten);
        Assert.Equal("1/2-1/2", gamesRewritten[0].Result);

        // 2. Save as new record (Id = 0)
        modifiedDb = null;
        var newRecordHeader = new GameHeader
        {
            Id = 0,
            White = "Smyslov, Vasily",
            Black = "Geller, Efim",
            Result = "0-1",
            Date = "1955.02.11",
            Pgn = "1. d4 d5 0-1"
        };

        long newId = await _dbManager.SaveGameAsync("RewriteTest", newRecordHeader);
        Assert.NotEqual(gameId1, newId);
        Assert.True(newId > gameId1);
        Assert.Equal("RewriteTest", modifiedDb);

        var (gamesFinal, countFinal) = await _dbManager.SearchGamesAsync("RewriteTest", new GameFilter());
        Assert.Equal(2, countFinal);
    }
}
