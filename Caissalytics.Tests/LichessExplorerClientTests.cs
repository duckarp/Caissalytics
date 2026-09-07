using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class LichessExplorerClientTests
{
    private const string SampleLichessResponse = """
    {
        "white": 1000,
        "draws": 500,
        "black": 500,
        "moves": [
            {
                "san": "e4",
                "uci": "e2e4",
                "white": 600,
                "draws": 300,
                "black": 300,
                "averageRating": 2600
            }
        ],
        "topGames": [
            {
                "id": "game123",
                "winner": "white",
                "white": { "name": "Carlsen, M", "rating": 2880 },
                "black": { "name": "Nakamura, H", "rating": 2875 },
                "year": 2024
            }
        ]
    }
    """;

    [Fact]
    public async Task LichessExplorerClient_CanBeConstructedWithStartedHttpClient()
    {
        var handler = new MockLichessHandler(HttpStatusCode.OK, SampleLichessResponse);
        var httpClient = new HttpClient(handler);
        // Fire request so client is in started state
        await httpClient.GetAsync("https://example.com");

        var ex = Record.Exception(() => new LichessExplorerClient(httpClient));
        Assert.Null(ex);
    }

    [Fact]
    public async Task QueryAsync_AttachesBearerToken_WhenTokenProvided()
    {
        var handler = new MockLichessHandler(HttpStatusCode.OK, SampleLichessResponse);
        var httpClient = new HttpClient(handler);
        var client = new LichessExplorerClient(httpClient);

        string testToken = "lip_test_secret_token_12345";
        var result = await client.QueryAsync("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", isMasters: true, apiToken: testToken);

        Assert.NotNull(result);
        Assert.NotNull(handler.LastRequest);
        Assert.NotNull(handler.LastRequest.Headers.Authorization);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization.Scheme);
        Assert.Equal(testToken, handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Contains("Caissalytics-Desktop/1.0", handler.LastRequest.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task QueryAsync_DoesNotAttachAuthHeader_WhenTokenIsEmpty()
    {
        var handler = new MockLichessHandler(HttpStatusCode.OK, SampleLichessResponse);
        var httpClient = new HttpClient(handler);
        var client = new LichessExplorerClient(httpClient);

        var result = await client.QueryAsync("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", isMasters: true, apiToken: null);

        Assert.NotNull(result);
        Assert.NotNull(handler.LastRequest);
        Assert.Null(handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task QueryAsync_ReturnsIsUnauthorized_OnHttp401()
    {
        var handler = new MockLichessHandler(HttpStatusCode.Unauthorized, "Unauthorized");
        var httpClient = new HttpClient(handler);
        var client = new LichessExplorerClient(httpClient);

        var result = await client.QueryAsync("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", isMasters: true, apiToken: null);

        Assert.NotNull(result);
        Assert.True(result.IsUnauthorized);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("token", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.CandidateMoves);
    }

    [Fact]
    public async Task QueryAsync_ParsesCandidateMovesAndTopGames_OnSuccess()
    {
        var handler = new MockLichessHandler(HttpStatusCode.OK, SampleLichessResponse);
        var httpClient = new HttpClient(handler);
        var client = new LichessExplorerClient(httpClient);

        var result = await client.QueryAsync("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", isMasters: true, apiToken: "lip_abc");

        Assert.NotNull(result);
        Assert.False(result.IsUnauthorized);
        Assert.Equal(2000, result.TotalPositionGames);
        Assert.Single(result.CandidateMoves);

        var move = result.CandidateMoves[0];
        Assert.Equal("e4", move.MoveSan);
        Assert.Equal("e2e4", move.MoveUci);
        Assert.Equal(1200, move.TotalGames);
        Assert.Equal(600, move.WhiteWins);
        Assert.Equal(300, move.Draws);
        Assert.Equal(300, move.BlackWins);
        Assert.Equal(2600, move.AvgRating);

        Assert.Single(result.TopGames);
        var game = result.TopGames[0];
        Assert.Equal("Carlsen, M", game.White);
        Assert.Equal("Nakamura, H", game.Black);
        Assert.Equal("1-0", game.Result);
        Assert.Equal("https://lichess.org/game123", game.Site);
    }

    [Fact]
    public void UserProfile_LichessApiToken_ReflectsTokenState()
    {
        var profile = new UserProfile();
        Assert.False(profile.HasLichessToken);
        Assert.Equal("", profile.LichessApiToken);

        profile.LichessApiToken = "lip_testtoken";
        Assert.True(profile.HasLichessToken);
    }

    private class MockLichessHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public HttpRequestMessage? LastRequest { get; private set; }

        public MockLichessHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
