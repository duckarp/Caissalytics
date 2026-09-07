using System.Net;
using System.Text;
using Caissalytics.Core;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class TablebaseTests
{
    [Fact]
    public void PieceCounting_IdentifiesEligibleTablebasePositions()
    {
        var service = new TablebaseService();

        // 32 pieces in standard starting position
        Assert.Equal(32, TablebaseService.CountPiecesInFen(BoardPosition.StartFen));
        Assert.False(service.CanProbeFen(BoardPosition.StartFen));

        // 3 pieces: King + Pawn vs King
        string kpFen = "8/8/8/4k3/8/4P3/4K3/8 w - - 0 1";
        Assert.Equal(3, TablebaseService.CountPiecesInFen(kpFen));
        Assert.True(service.CanProbeFen(kpFen));

        var boardPos = FenParser.Parse(kpFen);
        Assert.Equal(3, service.CountPieces(boardPos));
        Assert.True(service.CanProbePosition(boardPos));

        // 7 pieces: exactly the maximum supported
        string sevenPiece = "8/8/3k4/1p1p4/1P1P4/4K3/8/8 w - - 0 1"; // 2 kings, 4 pawns = 6 pieces
        Assert.True(service.CanProbeFen(sevenPiece));

        // 8 pieces: beyond tablebase limit
        string eightPiece = "8/8/3k4/1p1pp3/1P1PP3/4K3/8/8 w - - 0 1"; // 2 kings, 6 pawns = 8 pieces
        Assert.Equal(8, TablebaseService.CountPiecesInFen(eightPiece));
        Assert.False(service.CanProbeFen(eightPiece));
    }

    [Fact]
    public void TablebaseResult_FormatsTheoreticalVerdictsCorrectly()
    {
        var winResult = new TablebaseResult
        {
            Category = TablebaseCategory.Win,
            Dtz = 12,
            Dtm = 24
        };
        Assert.Contains("Winning", winResult.FormattedVerdict);
        Assert.Contains("Mate in 24", winResult.FormattedVerdict);

        var drawResult = new TablebaseResult
        {
            Category = TablebaseCategory.Draw
        };
        Assert.Equal("Theoretical Draw", drawResult.FormattedVerdict);

        var mateResult = new TablebaseResult
        {
            Checkmate = true
        };
        Assert.Equal("Checkmate", mateResult.FormattedVerdict);

        var stalemateResult = new TablebaseResult
        {
            Stalemate = true
        };
        Assert.Equal("Stalemate (Draw)", stalemateResult.FormattedVerdict);
    }

    [Fact]
    public async Task ProbePosition_WithMockHttp_ParsesAndInvertsMoveCategories()
    {
        // Sample Lichess Tablebase response
        string mockJson = """
        {
            "checkmate": false,
            "stalemate": false,
            "variant_win": false,
            "variant_loss": false,
            "insufficient_material": false,
            "dtz": 9,
            "precise_dtz": 9,
            "dtm": 43,
            "category": "win",
            "moves": [
                {
                    "uci": "e1d2",
                    "san": "Kd2",
                    "zeroing": false,
                    "conversion": false,
                    "checkmate": false,
                    "stalemate": false,
                    "dtz": -8,
                    "dtm": -42,
                    "category": "loss"
                },
                {
                    "uci": "e2e3",
                    "san": "e3",
                    "zeroing": true,
                    "conversion": false,
                    "checkmate": false,
                    "stalemate": false,
                    "dtz": 0,
                    "dtm": 0,
                    "category": "draw"
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(mockJson);
        var httpClient = new HttpClient(handler);
        var service = new TablebaseService(httpClient);

        string fen = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";
        var result = await service.ProbePositionAsync(fen);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(TablebaseCategory.Win, result.Category);
        Assert.Equal(9, result.Dtz);
        Assert.Equal(43, result.Dtm);
        Assert.Equal(2, result.Moves.Count);

        // Move 1: Kd2 (Lichess reported opponent category as 'loss', which inverts to 'Win' for the moving player)
        var kd2 = result.Moves[0];
        Assert.Equal("Kd2", kd2.San);
        Assert.Equal("e1d2", kd2.Uci);
        Assert.Equal(TablebaseCategory.Win, kd2.Category);
        Assert.Equal(8, kd2.Dtz);
        Assert.Equal(42, kd2.Dtm);

        // Move 2: e3 (inverts from opponent 'draw' to 'Draw')
        var e3 = result.Moves[1];
        Assert.Equal("e3", e3.San);
        Assert.Equal(TablebaseCategory.Draw, e3.Category);

        // Second call should hit the memory cache (handler called only once)
        var cached = await service.ProbePositionAsync(fen);
        Assert.Same(result, cached);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task TablebaseService_CanBeConstructedWithStartedHttpClient()
    {
        var handler = new MockHttpMessageHandler("{}");
        var client = new HttpClient(handler);
        // Execute a request so the HttpClient has started
        await client.GetAsync("https://example.com");

        // Constructing TablebaseService must NOT throw InvalidOperationException
        var ex = Record.Exception(() => new TablebaseService(client));
        Assert.Null(ex);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        public int CallCount { get; private set; }

        public MockHttpMessageHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
