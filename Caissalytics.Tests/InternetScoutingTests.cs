using System.Net;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class InternetScoutingTests : IDisposable
{
    private readonly string _testDir;
    private readonly DatabaseManager _dbManager;

    public InternetScoutingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"caissalytics_scouting_test_{Guid.NewGuid():N}");
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
        catch { }
    }

    [Fact]
    public void FideScoutingService_ParsePlayerCardHtml_ExtractsAllProfileFields()
    {
        string sampleHtml = @"
<!DOCTYPE html>
<html>
<body>
    <h1 class=""player-title"">Carlsen, Magnus</h1>
    <div class=""profile-standart profile-game"">
        <p>2823</p>
    </div>
    <div class=""profile-rapid profile-game"">
        <p>2803</p>
    </div>
    <div class=""profile-blitz profile-game"">
        <p>2860</p>
    </div>
    <div class=""profile-info"">
        <p class=""profile-info-id"">1503014</p>
        <div class=""profile-info-country"">
            <img src=""/images/flags/no.svg"">
            Norway
        </div>
        <p class=""profile-info-byear"">1990</p>
        <p class=""profile-info-sex"">Male</p>
        <div class=""profile-info-title"">
            <p>Grandmaster</p>
        </div>
    </div>
    <div class=""profile-ranks"">
        <div class=""profile-rank-block"">
            <h5>World Rank</h5>
            <div class=""profile-rank-row"">
                <h6>Active players</h6>
                <p>1</p>
            </div>
            <div class=""profile-rank-row"">
                <h6>All players</h6>
                <p>1</p>
            </div>
        </div>
        <div class=""profile-rank-block"">
            <h5>National Rank NOR</h5>
            <div class=""profile-rank-row"">
                <h6>Active players</h6>
                <p>1</p>
            </div>
        </div>
    </div>
</body>
</html>";

        var card = FideScoutingService.ParsePlayerCardHtml("1503014", sampleHtml);

        Assert.NotNull(card);
        Assert.Equal("1503014", card!.FideId);
        Assert.Equal("Carlsen, Magnus", card.FullName);
        Assert.Equal("Grandmaster", card.Title);
        Assert.Equal("GM", card.TitleAbbreviation);
        Assert.Equal("Norway", card.Federation);
        Assert.Equal("https://ratings.fide.com/images/flags/no.svg", card.FlagUrl);
        Assert.Equal(1990, card.BirthYear);
        Assert.Equal("Male", card.Gender);
        Assert.Equal(2823, card.StandardElo);
        Assert.Equal(2803, card.RapidElo);
        Assert.Equal(2860, card.BlitzElo);
        Assert.Equal(1, card.WorldRankActive);
        Assert.Equal(1, card.WorldRankAll);
        Assert.Equal(1, card.NationalRank);
        Assert.Equal("NOR", card.FederationCode);
        Assert.Equal("https://ratings.fide.com/profile/1503014", card.ProfileUrl);
    }

    [Fact]
    public void FideScoutingService_ParseSearchResultsHtml_ExtractsMultiplePlayers()
    {
        string sampleSearchHtml = @"
<div id=""search_results"">
<table class=""table-top"" id=""table_results"">
    <thead>
        <tr><th>FIDE ID</th><th>Name</th><th>Title</th><th>Tr.T.</th><th>Fed</th><th>Std.</th><th>Rpd.</th><th>Blz.</th><th>B-Year</th></tr>
    </thead>
    <tbody>
        <tr>
            <td data-label=""FIDEID"">1503014</td>
            <td data-label=""Name""><a href=""/profile/1503014"" class=""found_name"">Carlsen, Magnus</a></td>
            <td data-label=""title"">GM</td>
            <td></td>
            <td class=""flag-wrapper""><img src=""/svg/NOR.svg"">NOR</td>
            <td data-label=""Rtg"">2823</td>
            <td data-label=""Rtg"">2803</td>
            <td data-label=""Rtg"">2860</td>
            <td data-label=""B-Year"">1990</td>
        </tr>
        <tr>
            <td data-label=""FIDEID"">1500000</td>
            <td data-label=""Name""><a href=""/profile/1500000"" class=""found_name"">Carlsen, Henrik</a></td>
            <td data-label=""title"">FM</td>
            <td></td>
            <td class=""flag-wrapper""><img src=""/svg/NOR.svg"">NOR</td>
            <td data-label=""Rtg"">2050</td>
            <td data-label=""Rtg""></td>
            <td data-label=""Rtg""></td>
            <td data-label=""B-Year"">1965</td>
        </tr>
    </tbody>
</table>
</div>";

        var list = FideScoutingService.ParseSearchResultsHtml(sampleSearchHtml, limit: 10);

        Assert.Equal(2, list.Count);
        Assert.Equal("1503014", list[0].FideId);
        Assert.Equal("Carlsen, Magnus", list[0].FullName);
        Assert.Equal("GM", list[0].Title);
        Assert.Equal("NOR", list[0].Federation);
        Assert.Equal(2823, list[0].StandardElo);
        Assert.Equal(2803, list[0].RapidElo);
        Assert.Equal(2860, list[0].BlitzElo);
        Assert.Equal(1990, list[0].BirthYear);

        Assert.Equal("1500000", list[1].FideId);
        Assert.Equal("FM", list[1].Title);
        Assert.Equal(2050, list[1].StandardElo);
        Assert.Null(list[1].RapidElo);
    }

    [Fact]
    public void ChessResultsScoutingService_ParseTournamentTableHtml_ExtractsTournamentsAndLinks()
    {
        string sampleHtml = @"
<table id=""datenxx"">
    <tr class=""CRg1"">
        <td class=""CR""><a href=""tnr1258257.aspx?lan=1&amp;art=9&amp;snr=64"">Navara, David</a></td>
        <td class=""CR"">6079</td>
        <td class=""CR"">309788</td>
        <td class=""CR"">1. Novoborsky SK</td>
        <td class=""CR"">CZE</td>
        <td class=""CR""><a href=""tnr1258257.aspx?lan=1"">Czech Extraliga 2024/25</a></td>
        <td class=""CR"">2025/04/12</td>
        <td class=""CR"">8.5</td>
        <td class=""CR"">11</td>
        <td class=""CR"">140</td>
    </tr>
    <tr class=""CRg2"">
        <td class=""CR""><a href=""tnr1164372.aspx?lan=1&amp;art=9&amp;snr=5"">Navara, David</a></td>
        <td class=""CR"">6079</td>
        <td class=""CR"">309788</td>
        <td class=""CR"">1. Novoborsky SK</td>
        <td class=""CR"">CZE</td>
        <td class=""CR""><a href=""tnr1164372.aspx?lan=1"">European Individual Championship</a></td>
        <td class=""CR"">2024/11/19</td>
        <td class=""CR"">7.0</td>
        <td class=""CR"">11</td>
        <td class=""CR"">380</td>
    </tr>
</table>";

        var entries = ChessResultsScoutingService.ParseTournamentTableHtml(sampleHtml, "https://chess-results.com", limit: 10);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Czech Extraliga 2024/25", entries[0].TournamentName);
        Assert.Equal("2025/04/12", entries[0].EndDate);
        Assert.Equal("1. Novoborsky SK", entries[0].Club);
        Assert.Equal("CZE", entries[0].Federation);
        Assert.Equal("8.5", entries[0].ScoreOrRank);
        Assert.Equal("11", entries[0].Rounds);
        Assert.Equal("140", entries[0].TotalPlayers);
        Assert.Equal("https://chess-results.com/tnr1258257.aspx?lan=1", entries[0].TournamentUrl);
        Assert.Equal("https://chess-results.com/tnr1258257.aspx?lan=1&art=9&snr=64", entries[0].PlayerCardUrl);
    }

    [Fact]
    public void OpponentDossierService_ExtractLastName_HandlesVariousFormats()
    {
        Assert.Equal("Carlsen", OpponentDossierService.ExtractLastName("Carlsen, Magnus"));
        Assert.Equal("Navara", OpponentDossierService.ExtractLastName("David Navara"));
        Assert.Equal("Kasparov", OpponentDossierService.ExtractLastName("Kasparov"));
        Assert.Equal("", OpponentDossierService.ExtractLastName("   "));
    }

    [Fact]
    public async Task OpponentDossierService_GenerateDossierAsync_WithFideId_ResolvesAndScouts()
    {
        string db = "FideDossierDb";
        await _dbManager.CreateDatabaseAsync(db);

        // Seed a game under "Carlsen, Magnus"
        string pgn = @"[Event ""Wijk aan Zee""]
[Date ""2023.01.15""]
[White ""Carlsen, Magnus""]
[Black ""Caruana, Fabiano""]
[Result ""1-0""]
[WhiteElo ""2859""]
[BlackElo ""2790""]
[ECO ""C84""]

1. e4 e5 2. Nf3 Nc6 1-0";
        await _dbManager.ImportPgnTextAsync(db, pgn);

        var mockFide = new MockFideScoutingService();
        var mockChessResults = new MockChessResultsScoutingService();
        var dossierService = new OpponentDossierService(_dbManager, new MockHttpClientFactory(), mockFide, mockChessResults);

        // Search using numeric FIDE ID "1503014"
        var report = await dossierService.GenerateDossierAsync("1503014", db);

        Assert.NotNull(report);
        Assert.Equal("Carlsen, Magnus", report!.PlayerName);
        Assert.NotNull(report.FideCard);
        Assert.Equal("1503014", report.FideCard!.FideId);
        Assert.Equal("Grandmaster", report.FideCard.Title);
        Assert.Equal("GM", report.FideCard.TitleAbbreviation);
        Assert.Equal(2823, report.FideCard.StandardElo);
        Assert.NotEmpty(report.RecentTournaments);
        Assert.Equal("Sample Event", report.RecentTournaments[0].TournamentName);
        Assert.Equal(1, report.TotalGames);
        Assert.Equal(1, report.WhiteWins);
    }

    [Fact]
    public async Task OpponentDossierService_GenerateDossierAsync_ReturnsOnlineProfileEvenWhenZeroLocalGames()
    {
        string db = "EmptyDossierDb";
        await _dbManager.CreateDatabaseAsync(db);

        var mockFide = new MockFideScoutingService();
        var mockChessResults = new MockChessResultsScoutingService();
        var dossierService = new OpponentDossierService(_dbManager, new MockHttpClientFactory(), mockFide, mockChessResults);

        // Player "1503014" has no games in EmptyDossierDb
        var report = await dossierService.GenerateDossierAsync("1503014", db);

        Assert.NotNull(report);
        Assert.Equal("Carlsen, Magnus", report!.PlayerName);
        Assert.Equal(0, report.TotalGames);
        Assert.NotNull(report.FideCard);
        Assert.Equal("1503014", report.FideCard!.FideId);
        Assert.NotEmpty(report.RecentTournaments);
    }

    [Fact]
    public async Task OpponentDossierService_FetchChessResultsGamesAsync_ImportsGames()
    {
        string db = "CrImportDb";
        await _dbManager.CreateDatabaseAsync(db);
        await _dbManager.SetActiveDatabaseAsync(db);

        var mockFide = new MockFideScoutingService();
        var mockChessResults = new MockChessResultsScoutingService();
        var dossierService = new OpponentDossierService(_dbManager, new MockHttpClientFactory(), mockFide, mockChessResults);

        var result = await dossierService.FetchChessResultsGamesAsync("1503014", "Carlsen");

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.TotalImported);

        var (games, count) = await _dbManager.SearchGamesAsync(db, new GameFilter { Player = "Carlsen" });
        Assert.Equal(1, count);
    }

    [Fact]
    public void FideScoutingService_ParsePlayerCardHtml_UnratedBlitz_DoesNotLeakWorldRank()
    {
        string html = @"
<div class=""profile-games"">
    <div class=""profile-standart profile-game"">
        <img src=""/img/logo_std.svg"" height=25>
        <p>2058</p><p>STANDARD</p>
    </div>
    <div class=""profile-rapid profile-game"">
        <img src=""/img/logo_rpd.svg"" height=25>
        <p>1982</p><p>RAPID</p>
    </div>
    <div class=""profile-blitz profile-game"">
        <img src=""/img/logo_blitz.svg"" height=25>
        <p>Not rated</p><p>BLITZ</p>
    </div>
</div>
<div class=""profile-info"">
    <h1 class=""player-title"">Bielik, Jakub</h1>
    <p class=""profile-info-id"">73600938</p>
    <div class=""profile-info-title"">
        <p>None</p>
    </div>
</div>
<div class=""profile-ranks"">
    <div class=""profile-rank-block"">
        <h5>World Rank</h5>
        <div class=""profile-rank-row"">
            <h6>Active players</h6>
            <p>19925</p>
        </div>
    </div>
</div>";

        var card = FideScoutingService.ParsePlayerCardHtml("73600938", html);

        Assert.NotNull(card);
        Assert.Equal("Bielik, Jakub", card!.FullName);
        Assert.Equal(2058, card.StandardElo);
        Assert.Equal(1982, card.RapidElo);
        Assert.Null(card.BlitzElo); // Must NOT be 19925!
        Assert.Null(card.Title); // 'None' must be treated as no title
        Assert.Equal(19925, card.WorldRankActive);
    }

    [Fact]
    public async Task OpponentDossierService_ExtractMoves_HandlesCapturesAndDateHeadersCorrectly()
    {
        string db = "ScandinavianDb";
        await _dbManager.CreateDatabaseAsync(db);

        string samplePgn = @"[Event ""LJ Slovakia Chess Open 2026""]
[Site ""Liptovský Ján""]
[Date ""2024.11.16""]
[Round ""1""]
[White ""Bielik, Jakub""]
[Black ""Opponent, Test""]
[Result ""1-0""]

1. e4 d5 2. exd5 Qxd5 3. Nc3 Qd8 1-0

[Event ""LJ Slovakia Chess Open 2026""]
[Site ""Liptovský Ján""]
[Date ""2024.11.16""]
[Round ""2""]
[White ""Opponent, Test""]
[Black ""Bielik, Jakub""]
[Result ""0-1""]

1. e4 d5 2. exd5 Qxd5 3. Nc3 Qa5 0-1";

        await _dbManager.ImportPgnTextAsync(db, samplePgn);

        var mockFide = new MockFideScoutingService();
        var mockChessResults = new MockChessResultsScoutingService();
        var dossierService = new OpponentDossierService(_dbManager, new MockHttpClientFactory(), mockFide, mockChessResults);

        var report = await dossierService.GenerateDossierAsync("Bielik, Jakub", db);

        Assert.NotNull(report);
        Assert.Equal(2, report!.TotalGames);

        // White Repertoire should be 1. e4 (NOT 1. 16!)
        Assert.Single(report.WhiteRepertoire);
        Assert.Equal("1. e4", report.WhiteRepertoire[0].Move);

        // Key line should capture 2. exd5 Qxd5 (NOT 2. e!)
        Assert.NotEmpty(report.WhiteRepertoire[0].KeyLines);
        Assert.Contains("2. exd5", report.WhiteRepertoire[0].KeyLines[0].MoveSequence);

        // Black Repertoire should be vs 1. e4: 1... d5 (NOT vs 1. 16!)
        Assert.Single(report.BlackRepertoire);
        Assert.Equal("vs 1. e4: 1... d5", report.BlackRepertoire[0].Move);
    }

    private class MockFideScoutingService : IFideScoutingService
    {
        public Task<FidePlayerCard?> GetPlayerCardAsync(string fideId, CancellationToken cancellationToken = default)
        {
            var card = new FidePlayerCard
            {
                FideId = fideId,
                FullName = "Carlsen, Magnus",
                Title = "Grandmaster",
                Federation = "Norway",
                StandardElo = 2823,
                RapidElo = 2803,
                BlitzElo = 2860,
                BirthYear = 1990
            };
            return Task.FromResult<FidePlayerCard?>(card);
        }

        public Task<List<FideSearchResult>> SearchPlayersByNameAsync(string query, int limit = 15, CancellationToken cancellationToken = default)
        {
            var list = new List<FideSearchResult>
            {
                new()
                {
                    FideId = "1503014",
                    FullName = "Carlsen, Magnus",
                    Title = "GM",
                    Federation = "NOR",
                    StandardElo = 2823
                }
            };
            return Task.FromResult(list);
        }
    }

    private class MockChessResultsScoutingService : IChessResultsScoutingService
    {
        public Task<List<ChessResultsTournamentEntry>> SearchPlayerTournamentsAsync(string? fideId, string? lastName, int limit = 25, CancellationToken cancellationToken = default)
        {
            var list = new List<ChessResultsTournamentEntry>
            {
                new()
                {
                    TournamentName = "Sample Event",
                    EndDate = "2025/01/01",
                    ScoreOrRank = "9.0",
                    Rounds = "11",
                    TotalPlayers = "100",
                    TournamentUrl = "https://chess-results.com/tnr123.aspx",
                    PlayerCardUrl = "https://chess-results.com/tnr123.aspx?art=9&snr=1"
                }
            };
            return Task.FromResult(list);
        }

        public Task<string?> DownloadPlayerPgnsAsync(string? fideId, string? lastName, CancellationToken cancellationToken = default)
        {
            string pgn = @"[Event ""Chess-Results Test Event""]
[Site ""Prague""]
[Date ""2025.02.01""]
[White ""Carlsen, Magnus""]
[Black ""Opponent, Test""]
[Result ""1-0""]

1. e4 c5 2. Nf3 1-0";
            return Task.FromResult<string?>(pgn);
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
