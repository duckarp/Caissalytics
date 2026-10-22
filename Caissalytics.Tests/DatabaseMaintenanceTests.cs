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
    public async Task DatabaseWithImportRunning_CannotBeDeletedOrRenamed()
    {
        var db = new DatabaseManager(_dir);
        await db.CreateDatabaseAsync("Importing");

        // A PGN stream that blocks after the first game until released
        var gate = new SemaphoreSlim(0);
        var stream = new GatedPgnStream("[Event \"E\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"*\"]\n\n1. e4 *\n\n", gate);
        var import = db.ImportPgnStreamAsync("Importing", stream);

        await Task.Delay(200);
        Assert.True(db.IsImportInProgress("Importing"));
        await Assert.ThrowsAsync<DatabaseBusyException>(() => db.DeleteDatabaseAsync("Importing"));
        await Assert.ThrowsAsync<DatabaseBusyException>(() => db.RenameDatabaseAsync("Importing", "Renamed"));

        gate.Release();
        await import.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(db.IsImportInProgress("Importing"));
        Assert.True(await db.DeleteDatabaseAsync("Importing"));
    }

    /// <summary>Serves the given text, then blocks the next read until the gate is released (then reports end of stream).</summary>
    private sealed class GatedPgnStream(string text, SemaphoreSlim gate) : Stream
    {
        private readonly byte[] _data = System.Text.Encoding.UTF8.GetBytes(text);
        private int _pos;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pos >= _data.Length)
            {
                await gate.WaitAsync(cancellationToken);
                return 0;
            }
            int n = Math.Min(buffer.Length, _data.Length - _pos);
            _data.AsMemory(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task DeleteGames_RemovesGamesAndPositionsInOneBatch()
    {
        var db = new DatabaseManager(_dir);
        await db.CreateDatabaseAsync("Batch");
        long a = await db.SaveGameAsync("Batch", new GameHeader { White = "A", Black = "B", Result = "*", Pgn = "1. e4 e5 *" });
        long b = await db.SaveGameAsync("Batch", new GameHeader { White = "C", Black = "D", Result = "*", Pgn = "1. e4 c5 *" });
        long c = await db.SaveGameAsync("Batch", new GameHeader { White = "E", Black = "F", Result = "*", Pgn = "1. d4 d5 *" });

        int modifiedEvents = 0;
        db.OnDatabaseModified += _ => modifiedEvents++;

        int deleted = await db.DeleteGamesAsync("Batch", [a, b, 999_999]);

        Assert.Equal(2, deleted);
        Assert.Equal(1, modifiedEvents);
        var (games, total) = await db.SearchGamesAsync("Batch", new GameFilter());
        Assert.Equal(1, total);
        Assert.Equal(c, Assert.Single(games).Id);
        var afterE4 = await db.QueryPositionAsync("Batch", KeyOf("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"));
        Assert.Equal(0, afterE4.TotalPositionGames);
    }

    [Fact]
    public async Task Import_IgnoresSemicolonCommentsAndEscapeLines()
    {
        var db = new DatabaseManager(_dir);
        await db.CreateDatabaseAsync("Imported");

        // "d5" in the ';' comment is a legal reply to 1.e4 and must not be indexed as the move played
        const string pgn = "[Event \"Club\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"*\"]\n\n%tool output Nf3\n1. e4 ; d5 looks fine\ne5 2. Nf3 *\n";
        await db.ImportPgnTextAsync("Imported", pgn);

        var (games, _) = await db.SearchGamesAsync("Imported", new GameFilter());
        Assert.Equal(3, Assert.Single(games).PlyCount);

        var afterE4E5 = await db.QueryPositionAsync("Imported", KeyOf("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2"));
        Assert.Equal(1, afterE4E5.TotalPositionGames);
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
