using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class DatabaseExportTests : IDisposable
{
    private readonly string _testDir;
    private readonly DatabaseManager _dbManager;

    private const string GameA =
@"[Event ""Export A""]
[White ""AlphaOne""]
[Black ""BetaOne""]
[Date ""2026.01.01""]
[ECO ""B30""]
[Result ""1-0""]

1. e4 c5 2. Nf3 Nc6 1-0";

    private const string GameB =
@"[Event ""Export B""]
[White ""AlphaTwo""]
[Black ""BetaTwo""]
[Date ""2026.01.02""]
[ECO ""C50""]
[Result ""0-1""]

1. e4 e5 2. Nf3 Nc6 3. Bc4 0-1";

    private const string GameC =
@"[Event ""Export C""]
[White ""AlphaThree""]
[Black ""BetaThree""]
[Date ""2026.01.03""]
[ECO ""D71""]
[Result ""1/2-1/2""]

1. d4 Nf6 2. c4 g6 3. g3 d5 1/2-1/2";

    public DatabaseExportTests()
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

    private Task SeedDatabaseAsync(string name = "ExportDb")
    {
        string pgn = GameA + "\n\n" + GameB + "\n\n" + GameC;
        return _dbManager.ImportPgnTextAsync(name, pgn);
    }

    [Fact]
    public async Task ExportAll_ReturnsAllGamesNewestFirst()
    {
        await SeedDatabaseAsync();

        var chunks = new List<string>();
        int count = await _dbManager.ExportGamesToPgnAsync("ExportDb", null, c =>
        {
            chunks.Add(c);
            return Task.CompletedTask;
        });

        Assert.Equal(3, count);

        string pgn = string.Concat(chunks);
        int idxC = pgn.IndexOf("[Event \"Export C\"]");
        int idxB = pgn.IndexOf("[Event \"Export B\"]");
        int idxA = pgn.IndexOf("[Event \"Export A\"]");

        Assert.True(idxC >= 0 && idxB > idxC && idxA > idxB, $"Expected order C -> B -> A, got C@{idxC} B@{idxB} A@{idxA}");
        Assert.Contains("1. d4 Nf6 2. c4 g6 3. g3 d5 1/2-1/2", pgn);
        Assert.Contains("1. e4 e5 2. Nf3 Nc6 3. Bc4 0-1", pgn);
        Assert.Contains("1. e4 c5 2. Nf3 Nc6 1-0", pgn);
    }

    [Fact]
    public async Task ExportAll_GamesSeparatedBySingleBlankLine()
    {
        await SeedDatabaseAsync();

        var (games, _) = await _dbManager.SearchGamesAsync("ExportDb", new GameFilter());
        string storedA = (await _dbManager.GetGameByIdAsync("ExportDb", games.First(g => g.White == "AlphaOne").Id))!.Pgn.Trim();
        string storedB = (await _dbManager.GetGameByIdAsync("ExportDb", games.First(g => g.White == "AlphaTwo").Id))!.Pgn.Trim();
        string storedC = (await _dbManager.GetGameByIdAsync("ExportDb", games.First(g => g.White == "AlphaThree").Id))!.Pgn.Trim();

        var chunks = new List<string>();
        await _dbManager.ExportGamesToPgnAsync("ExportDb", null, c =>
        {
            chunks.Add(c);
            return Task.CompletedTask;
        });
        string pgn = string.Concat(chunks);

        Assert.Equal(storedC + "\n\n" + storedB + "\n\n" + storedA + "\n", pgn);
    }

    [Fact]
    public async Task ExportFiltered_OnlyMatchingGamesAreExported()
    {
        await SeedDatabaseAsync();

        var chunks = new List<string>();
        int count = await _dbManager.ExportGamesToPgnAsync("ExportDb",
            new GameFilter { Player = "AlphaTwo", Result = "0-1", Eco = "C50" },
            c =>
            {
                chunks.Add(c);
                return Task.CompletedTask;
            });

        Assert.Equal(1, count);

        string pgn = string.Concat(chunks);
        Assert.Contains("Export B", pgn);
        Assert.DoesNotContain("Export A", pgn);
        Assert.DoesNotContain("Export C", pgn);
    }

    [Fact]
    public async Task ExportFiltered_NoMatches_ReturnsZeroWithoutChunks()
    {
        await SeedDatabaseAsync();

        var chunks = new List<string>();
        int count = await _dbManager.ExportGamesToPgnAsync("ExportDb",
            new GameFilter { Eco = "Z99" },
            c =>
            {
                chunks.Add(c);
                return Task.CompletedTask;
            });

        Assert.Equal(0, count);
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ExportUnknownDatabase_ReturnsZeroWithoutChunks()
    {
        var chunks = new List<string>();
        int count = await _dbManager.ExportGamesToPgnAsync("DoesNotExist", null, c =>
        {
            chunks.Add(c);
            return Task.CompletedTask;
        });

        Assert.Equal(0, count);
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ExportEmptyDatabase_ReturnsZeroWithoutChunks()
    {
        await _dbManager.CreateDatabaseAsync("EmptyDb");

        var chunks = new List<string>();
        int count = await _dbManager.ExportGamesToPgnAsync("EmptyDb", null, c =>
        {
            chunks.Add(c);
            return Task.CompletedTask;
        });

        Assert.Equal(0, count);
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ExportAll_ReportsProgressFromZeroToTotal()
    {
        await SeedDatabaseAsync();

        // A plain IProgress (not Progress<T>) so intermediate reports are not coalesced.
        var progress = new RecordingProgress();

        int count = await _dbManager.ExportGamesToPgnAsync("ExportDb", null, _ => Task.CompletedTask, progress);

        Assert.Equal(3, count);
        Assert.Equal((0, 3, "Exporting…"), progress.Reports.First());
        Assert.Equal((3, 3, "Exporting… (3/3)"), progress.Reports.Last());
    }

    private sealed class RecordingProgress : IProgress<(int Current, int Total, string Status)>
    {
        public List<(int Current, int Total, string Status)> Reports { get; } = new();
        public void Report((int Current, int Total, string Status) value) => Reports.Add(value);
    }

    [Fact]
    public async Task ExportAll_CancelledToken_ThrowsOperationCanceled()
    {
        await SeedDatabaseAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _dbManager.ExportGamesToPgnAsync("ExportDb", null, _ => Task.CompletedTask, cancellationToken: cts.Token));
    }
}
