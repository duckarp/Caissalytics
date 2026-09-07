using System.Net;
using System.Text;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class OpponentDossierTests : IDisposable
{
    private readonly string _testDir;
    private readonly DatabaseManager _dbManager;
    private readonly OpponentDossierService _dossierService;

    public OpponentDossierTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"caissalytics_dossier_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _dbManager = new DatabaseManager(_testDir);

        // Mock HttpClientFactory
        var mockFactory = new TestHttpClientFactory();
        _dossierService = new OpponentDossierService(_dbManager, mockFactory);
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
        catch { }
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new TestHttpMessageHandler());
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[Event \"Test\"]\n[White \"TestUser\"]\n[Black \"Opponent\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 1-0")
            };
            return Task.FromResult(response);
        }
    }

    private async Task SeedSampleGamesAsync(string dbName)
    {
        await _dbManager.CreateDatabaseAsync(dbName);

        string samplePgn = @"
[Event ""World Championship""]
[Site ""London""]
[Date ""1995.10.01""]
[White ""Kasparov, Garry""]
[Black ""Anand, Viswanathan""]
[Result ""1-0""]
[WhiteElo ""2805""]
[BlackElo ""2725""]
[ECO ""B90""]

1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 1-0

[Event ""World Championship""]
[Site ""London""]
[Date ""1995.10.03""]
[White ""Kasparov, Garry""]
[Black ""Anand, Viswanathan""]
[Result ""0-1""]
[WhiteElo ""2805""]
[BlackElo ""2725""]
[ECO ""B90""]

1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 0-1

[Event ""World Championship""]
[Site ""London""]
[Date ""1995.10.05""]
[White ""Kasparov, Garry""]
[Black ""Anand, Viswanathan""]
[Result ""1-0""]
[WhiteElo ""2815""]
[BlackElo ""2725""]
[ECO ""E60""]

1. d4 Nf6 2. c4 g6 3. Nc3 Bg7 1-0

[Event ""Linares""]
[Site ""Linares""]
[Date ""1999.03.01""]
[White ""Topalov, Veselin""]
[Black ""Kasparov, Garry""]
[Result ""0-1""]
[WhiteElo ""2700""]
[BlackElo ""2812""]
[ECO ""B07""]

1. e4 d6 2. d4 Nf6 3. Nc3 g6 0-1

[Event ""Linares""]
[Site ""Linares""]
[Date ""1999.03.05""]
[White ""Kramnik, Vladimir""]
[Black ""Kasparov, Garry""]
[Result ""1/2-1/2""]
[WhiteElo ""2751""]
[BlackElo ""2812""]
[ECO ""E04""]

1. d4 Nf6 2. c4 e6 3. g3 d5 1/2-1/2
";
        await _dbManager.ImportPgnTextAsync(dbName, samplePgn);
    }

    [Fact]
    public async Task GenerateDossierAsync_ComputesAccurateRecordAndPerformance()
    {
        string db = "DossierTestDb";
        await SeedSampleGamesAsync(db);

        var report = await _dossierService.GenerateDossierAsync("Kasparov", db);

        Assert.NotNull(report);
        Assert.Equal("Kasparov", report!.PlayerName);
        Assert.Equal(5, report.TotalGames);

        // White: 3 games (2 wins, 1 loss) -> Score = 66.7%
        Assert.Equal(3, report.WhiteGamesCount);
        Assert.Equal(2, report.WhiteWins);
        Assert.Equal(0, report.WhiteDraws);
        Assert.Equal(1, report.WhiteLosses);
        Assert.Equal(66.7, report.WhiteScore);

        // Black: 2 games (1 win, 1 draw) -> Score = 75.0%
        Assert.Equal(2, report.BlackGamesCount);
        Assert.Equal(1, report.BlackWins);
        Assert.Equal(1, report.BlackDraws);
        Assert.Equal(0, report.BlackLosses);
        Assert.Equal(75.0, report.BlackScore);

        // Total: 3 wins, 1 draw, 1 loss -> Score = 70.0%
        Assert.Equal(3, report.TotalWins);
        Assert.Equal(1, report.TotalDraws);
        Assert.Equal(1, report.TotalLosses);
        Assert.Equal(70.0, report.OverallScore);

        // Peak Elo
        Assert.Equal(2815, report.PeakElo);
    }

    [Fact]
    public async Task GenerateDossierAsync_AnalyzesWhiteAndBlackRepertoire()
    {
        string db = "RepertoireTestDb";
        await SeedSampleGamesAsync(db);

        var report = await _dossierService.GenerateDossierAsync("Kasparov", db);

        Assert.NotNull(report);

        // White Repertoire branches: 1. e4 (2 games), 1. d4 (1 game)
        Assert.NotEmpty(report!.WhiteRepertoire);
        var e4Branch = report.WhiteRepertoire.FirstOrDefault(b => b.Move == "1. e4");
        Assert.NotNull(e4Branch);
        Assert.Equal(2, e4Branch!.GameCount);
        Assert.Equal(1, e4Branch.Wins);
        Assert.Equal(1, e4Branch.Losses);
        Assert.Equal(50.0, e4Branch.ScorePct);

        var d4Branch = report.WhiteRepertoire.FirstOrDefault(b => b.Move == "1. d4");
        Assert.NotNull(d4Branch);
        Assert.Equal(1, d4Branch!.GameCount);
        Assert.Equal(1, d4Branch.Wins);
        Assert.Equal(100.0, d4Branch.ScorePct);

        // Black Repertoire branches: vs 1. e4: 1... d6 and vs 1. d4: 1... Nf6
        Assert.NotEmpty(report.BlackRepertoire);
        var vsE4 = report.BlackRepertoire.FirstOrDefault(b => b.Move.Contains("1... d6"));
        Assert.NotNull(vsE4);
        Assert.Equal(1, vsE4!.GameCount);
        Assert.Equal(1, vsE4.Wins);
        Assert.Equal(100.0, vsE4.ScorePct);
    }

    [Fact]
    public async Task SearchKnownPlayersAsync_FindsMatchingPlayers()
    {
        string db = "SearchPlayerTestDb";
        await SeedSampleGamesAsync(db);

        var players = await _dossierService.SearchKnownPlayersAsync("Anand", db);

        Assert.NotEmpty(players);
        Assert.Contains(players, p => p.Contains("Anand"));
    }

    [Fact]
    public async Task GenerateDossierAsync_ReturnsNullForUnknownPlayer()
    {
        string db = "EmptyTestDb";
        await _dbManager.CreateDatabaseAsync(db);

        var report = await _dossierService.GenerateDossierAsync("NonExistentPlayerXYZ", db);
        Assert.Null(report);
    }
}
