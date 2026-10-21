using Caissalytics.Core;
using Caissalytics.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Caissalytics.Tests;

public class DatabaseMaintenanceTests : IDisposable
{
    private const string EndgameFen = "8/8/8/4k3/8/8/4P3/4K3 w - - 0 1";
    private const string EndgamePgn = "[Event \"Endgame\"]\n[SetUp \"1\"]\n[FEN \"" + EndgameFen + "\"]\n\n1. e4+ Kd6 *";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"caissalytics_dbmaint_{Guid.NewGuid():N}");

    public DatabaseMaintenanceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_dir, true);
        }
        catch { }
    }

    private static ulong KeyOf(string fen) => FenParser.Parse(fen).ZobristKey;

    [Fact]
    public async Task DeletingAnotherDatabase_KeepsTheActiveOne()
    {
        var db = new DatabaseManager(_dir);
        string[] names = ["Alpha", "Beta", "Gamma"];
        foreach (var name in names)
        {
            await db.CreateDatabaseAsync(name);
        }

        // The old code switched to whichever file the directory listing returned first, so try
        // every database as the active one: at least one of them is not first in the listing
        foreach (var active in names)
        {
            await db.CreateDatabaseAsync("Scratch");
            await db.SetActiveDatabaseAsync(active);

            await db.DeleteDatabaseAsync("Scratch");

            Assert.Equal(active, (await db.GetActiveDatabaseAsync()).Name);
        }
    }

    [Fact]
    public async Task DatabaseNameWithConnectionStringCharacters_StillOpens()
    {
        var db = new DatabaseManager(_dir);
        await db.CreateDatabaseAsync("Club;Season=2024");

        await db.SaveGameAsync("Club;Season=2024", new GameHeader { White = "A", Black = "B", Result = "1-0", Pgn = "1. e4 e5 1-0" });
        var (games, total) = await db.SearchGamesAsync("Club;Season=2024", new GameFilter());

        Assert.Equal(1, total);
        Assert.Single(games);
    }

    [Fact]
    public async Task SavedGameWithFenStart_IsIndexedFromItsOwnStartPosition()
    {
        var db = new DatabaseManager(_dir);
        await db.CreateDatabaseAsync("Endgames");

        await db.SaveGameAsync("Endgames", new GameHeader { White = "A", Black = "B", Result = "*", Pgn = EndgamePgn });
        var result = await db.QueryPositionAsync("Endgames", KeyOf(EndgameFen));

        Assert.Equal(1, result.TotalPositionGames);
    }

    [Fact]
    public async Task LegacyDatabase_BackfillReachesGamesBehindManyUnindexableOnes()
    {
        // A database from before the position index: games but no positions table rows.
        // 600 games without moves come first; the old backfill took 500 at a time and never got past them.
        string path = Path.Combine(_dir, "Legacy.db");
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            using var create = conn.CreateCommand();
            create.Transaction = tx;
            create.CommandText = @"
                CREATE TABLE games (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    white TEXT NOT NULL, black TEXT NOT NULL,
                    white_elo INTEGER, black_elo INTEGER,
                    result TEXT NOT NULL, date TEXT, event TEXT, site TEXT, round TEXT, eco TEXT,
                    ply_count INTEGER DEFAULT 0, pgn TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync();

            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO games (white, black, result, pgn) VALUES ('W', 'B', '*', $pgn);";
            var pPgn = insert.Parameters.Add("$pgn", SqliteType.Text);
            for (int i = 0; i < 600; i++)
            {
                pPgn.Value = "[Event \"Empty\"]\n\n*";
                await insert.ExecuteNonQueryAsync();
            }
            pPgn.Value = "[Event \"Open\"]\n\n1. e4 e5 *";
            await insert.ExecuteNonQueryAsync();
            pPgn.Value = EndgamePgn;
            await insert.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        SqliteConnection.ClearAllPools();

        var db = new DatabaseManager(_dir);
        var afterE4 = await db.QueryPositionAsync("Legacy", KeyOf("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"));
        var endgame = await db.QueryPositionAsync("Legacy", KeyOf(EndgameFen));

        Assert.Equal(1, afterE4.TotalPositionGames);
        Assert.Equal(1, endgame.TotalPositionGames);
    }
}
