using Caissalytics.Core;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class TimeControlInfoTests : IDisposable
{
    private readonly string _testDir;
    private readonly DatabaseManager _dbManager;

    public TimeControlInfoTests()
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
    public void ExtractRaw_WithTag_ReturnsValue()
    {
        string pgn =
            "[Event \"Test\"]\n" +
            "[Site \"https://lichess.org/abc\"]\n" +
            "[TimeControl \"180+2\"]\n" +
            "[White \"White\"]\n" +
            "[Black \"Black\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. e4 1-0\n";
        Assert.Equal("180+2", TimeControlInfo.ExtractRaw(pgn));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[Event \"x\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"1-0\"]\n\n1. e4 1-0")]
    public void ExtractRaw_WithoutTag_ReturnsNull(string? pgn)
    {
        Assert.Null(TimeControlInfo.ExtractRaw(pgn));
    }

    [Theory]
    [InlineData("[TimeControl \"\"]")]
    [InlineData("[TimeControl \"?\"]")]
    [InlineData("[TimeControl \"*\"]")]
    public void ExtractRaw_EmptyOrUnknownValue_ReturnsNull(string pgn)
    {
        Assert.Null(TimeControlInfo.ExtractRaw(pgn));
    }

    [Theory]
    [InlineData("180+2", 180)]
    [InlineData("300", 300)]
    [InlineData("0:30", 30)]
    [InlineData("5:30", 330)]
    [InlineData("45:00+10", 2700)]
    [InlineData("1:00:00", 3600)]
    [InlineData("40/850400 36/873840", 1724)]
    [InlineData("40/3240000 20/1440000 0/1800000", 6480)]
    [InlineData("30/1200000", 1200)]
    [InlineData("30/1800", 1800)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public void BaseSeconds_ParsesAllFormats(string? raw, int? expected)
    {
        Assert.Equal(expected, TimeControlInfo.BaseSeconds(raw));
    }

    [Theory]
    [InlineData(179, "bullet")]
    [InlineData(180, "blitz")]
    [InlineData(599, "blitz")]
    [InlineData(600, "rapid")]
    [InlineData(3599, "rapid")]
    [InlineData(3600, "standard")]
    [InlineData(null, "")]
    public void Classify_Boundaries(int? baseSeconds, string expected)
    {
        Assert.Equal(expected, TimeControlInfo.Classify(baseSeconds));
    }

    [Fact]
    public void GameHeader_TimeControl_And_TimeClass_FromPgn()
    {
        var game = new GameHeader
        {
            Pgn =
                "[Event \"Test\"]\n" +
                "[TimeControl \"180+2\"]\n" +
                "[White \"White\"]\n" +
                "[Black \"Black\"]\n" +
                "[Result \"1-0\"]\n" +
                "\n" +
                "1. e4 1-0\n"
        };
        Assert.Equal("180+2", game.TimeControl);
        Assert.Equal("blitz", game.TimeClass);

        var rapid = new GameHeader
        {
            Pgn =
                "[Event \"Test\"]\n" +
                "[TimeControl \"45:00+10\"]\n" +
                "[White \"White\"]\n" +
                "[Black \"Black\"]\n" +
                "[Result \"1-0\"]\n" +
                "\n" +
                "1. e4 1-0\n"
        };
        Assert.Equal("45:00+10", rapid.TimeControl);
        Assert.Equal("rapid", rapid.TimeClass);

        var none = new GameHeader
        {
            Pgn = "[White \"White\"]\n[Black \"Black\"]\n[Result \"1-0\"]\n\n1. e4 1-0\n"
        };
        Assert.Equal(string.Empty, none.TimeControl);
        Assert.Equal(string.Empty, none.TimeClass);
    }

    [Fact]
    public async Task ImportPgn_TimeControl_PreservedAndClassified()
    {
        await _dbManager.CreateDatabaseAsync("TimeImport");

        string pgn =
            "[Event \"Time 1\"]\n" +
            "[White \"WhiteOne\"]\n" +
            "[Black \"BlackOne\"]\n" +
            "[TimeControl \"25:00+5\"]\n" +
            "[Date \"2026.01.01\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. e4 e5 1-0\n" +
            "\n" +
            "[Event \"Time 2\"]\n" +
            "[White \"WhiteTwo\"]\n" +
            "[Black \"BlackTwo\"]\n" +
            "[Date \"2026.01.02\"]\n" +
            "[Result \"0-1\"]\n" +
            "\n" +
            "1. d4 Nf6 0-1\n";

        await _dbManager.ImportPgnTextAsync("TimeImport", pgn);

        var (games, _) = await _dbManager.SearchGamesAsync("TimeImport", new GameFilter());
        Assert.Equal(2, games.Count);

        var tagged = games.First(g => g.White == "WhiteOne");
        Assert.Equal("25:00+5", tagged.TimeControl);
        Assert.Equal("rapid", tagged.TimeClass);

        var untagged = games.First(g => g.White == "WhiteTwo");
        Assert.Equal(string.Empty, untagged.TimeControl);
        Assert.Equal(string.Empty, untagged.TimeClass);

        // The tag survives the round trip in the stored PGN.
        var storedPgn = (await _dbManager.GetGameByIdAsync("TimeImport", tagged.Id))!.Pgn;
        Assert.Contains("[TimeControl \"25:00+5\"]", storedPgn);
    }

    [Fact]
    public async Task SearchGames_TimeClassFilter_MatchesOnlyThatClass()
    {
        await _dbManager.CreateDatabaseAsync("TimeFilter");

        string pgn =
            "[Event \"T\"]\n" +
            "[White \"BulletOne\"]\n" +
            "[Black \"BlackOne\"]\n" +
            "[TimeControl \"1:30\"]\n" +
            "[Date \"2026.02.01\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. e4 e5 1-0\n" +
            "\n" +
            "[Event \"T\"]\n" +
            "[White \"BlitzOne\"]\n" +
            "[Black \"BlackOne\"]\n" +
            "[TimeControl \"5:00\"]\n" +
            "[Date \"2026.02.02\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. e4 e5 1-0\n" +
            "\n" +
            "[Event \"T\"]\n" +
            "[White \"RapidOne\"]\n"
            + "[Black \"BlackOne\"]\n" +
            "[TimeControl \"25:00+5\"]\n" +
            "[Date \"2026.02.03\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. e4 e5 1-0\n" +
            "\n" +
            "[Event \"T\"]\n" +
            "[White \"StandardOne\"]\n" +
            "[Black \"BlackOne\"]\n" +
            "[TimeControl \"1:30:00\"]\n" +
            "[Date \"2026.02.04\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. e4 e5 1-0\n" +
            "\n" +
            "[Event \"T\"]\n" +
            "[White \"NoTimeOne\"]\n" +
            "[Black \"BlackOne\"]\n" +
            "[Date \"2026.02.05\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. e4 e5 1-0\n";

        await _dbManager.ImportPgnTextAsync("TimeFilter", pgn);

        foreach (string expected in new[] { "bullet", "blitz", "rapid", "standard" })
        {
            var (games, total) = await _dbManager.SearchGamesAsync(
                "TimeFilter", new GameFilter { TimeClass = expected });
            Assert.Equal(1, total);
            Assert.Equal(expected, games[0].TimeClass);
        }

        var (all, allTotal) = await _dbManager.SearchGamesAsync(
            "TimeFilter", new GameFilter { TimeClass = "all" });
        Assert.Equal(5, allTotal);
    }

    [Fact]
    public async Task ExportGames_TimeClassFilter_ExportsOnlyMatching()
    {
        await _dbManager.CreateDatabaseAsync("TimeExport");

        string pgn =
            "[Event \"E\"]\n" +
            "[White \"RapidOne\"]\n" +
            "[Black \"BlackOne\"]\n" +
            "[TimeControl \"25:00+5\"]\n" +
            "[Date \"2026.03.01\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. e4 e5 1-0\n" +
            "\n" +
            "[Event \"E\"]\n" +
            "[White \"NoTimeOne\"]\n" +
            "[Black \"BlackTwo\"]\n" +
            "[Date \"2026.03.02\"]\n" +
            "[Result \"1-0\"]\n" +
            "\n" +
            "1. d4 d5 1-0\n";

        await _dbManager.ImportPgnTextAsync("TimeExport", pgn);

        var chunks = new List<string>();
        int exported = await _dbManager.ExportGamesToPgnAsync(
            "TimeExport", new GameFilter { TimeClass = "rapid" }, async c => chunks.Add(c));

        Assert.Equal(1, exported);
        string joined = string.Join("", chunks);
        Assert.Contains("RapidOne", joined);
        Assert.DoesNotContain("BlackTwo", joined);
    }
}
