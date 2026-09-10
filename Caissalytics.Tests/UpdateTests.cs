using System.Text.Json;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V2.0.0", "2.0.0")]
    [InlineData("1.0.0+build123", "1.0.0")]
    [InlineData("v2.1.0-beta.1", "2.1.0")]
    [InlineData("v1.0.0-rc1+2026", "1.0.0")]
    [InlineData("  v1.5.0  ", "1.5.0")]
    [InlineData("", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void CleanVersion_NormalizesTagsCorrectly(string? input, string expected)
    {
        string actual = SemVerHelper.CleanVersion(input!);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    public void IsNewerVersion_ComparesSemVerCorrectly(string latest, string current, bool expected)
    {
        bool actual = SemVerHelper.IsNewerVersion(latest, current);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FormattedAssetSize_ReturnsExpectedUnits()
    {
        var info = new UpdateInfo { AssetSizeBytes = 0 };
        Assert.Equal(string.Empty, info.FormattedAssetSize);

        info.AssetSizeBytes = 512 * 1024;
        Assert.Equal("512.0 KB", info.FormattedAssetSize);

        info.AssetSizeBytes = 15 * 1024 * 1024;
        Assert.Equal("15.0 MB", info.FormattedAssetSize);
    }

    [Fact]
    public void ParseReleaseElement_NewerRelease_SetsUpdateAvailable()
    {
        string json = """
        {
            "tag_name": "v1.2.0",
            "name": "Caissalytics 1.2.0 - Grandmaster Edition",
            "body": "### Features\n* Added auto-updater\n* Added Stockfish 18 support",
            "html_url": "https://github.com/duckarp/Caissalytics/releases/tag/v1.2.0",
            "published_at": "2026-09-08T12:00:00Z",
            "assets": [
                {
                    "name": "Caissalytics-v1.2.0-win-x64.zip",
                    "browser_download_url": "https://github.com/duckarp/Caissalytics/releases/download/v1.2.0/Caissalytics-v1.2.0-win-x64.zip",
                    "size": 52428800
                },
                {
                    "name": "Caissalytics-v1.2.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/tomask/Caissalytics/releases/download/v1.2.0/Caissalytics-v1.2.0-linux-x64.tar.gz",
                    "size": 50000000
                }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var updateInfo = new UpdateInfo();

        UpdateService.ParseReleaseElement(doc.RootElement, updateInfo, currentVer: "1.0.0");

        Assert.True(updateInfo.IsUpdateAvailable);
        Assert.Equal("1.2.0", updateInfo.LatestVersion);
        Assert.Equal("v1.2.0", updateInfo.TagName);
        Assert.Equal("Caissalytics 1.2.0 - Grandmaster Edition", updateInfo.ReleaseTitle);
        Assert.Contains("Added auto-updater", updateInfo.ReleaseNotes);
        Assert.Equal("https://github.com/duckarp/Caissalytics/releases/tag/v1.2.0", updateInfo.ReleaseUrl);
        Assert.NotNull(updateInfo.AssetDownloadUrl);
        Assert.NotNull(updateInfo.AssetFileName);
        Assert.True(updateInfo.AssetSizeBytes > 0);
    }

    [Fact]
    public void ParseReleaseElement_SameOrOlderRelease_SetsUpdateUnavailable()
    {
        string json = """
        {
            "tag_name": "v1.0.0",
            "name": "Initial Release",
            "body": "First release",
            "html_url": "https://github.com/tomask/Caissalytics/releases/tag/v1.0.0",
            "assets": []
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var updateInfo = new UpdateInfo();

        UpdateService.ParseReleaseElement(doc.RootElement, updateInfo, currentVer: "1.0.0");

        Assert.False(updateInfo.IsUpdateAvailable);
        Assert.Contains("up to date", updateInfo.StatusMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_RetrievesLatestReleaseMetadata()
    {
        string fakeJson = """
        {
            "tag_name": "v1.4.0",
            "name": "Caissalytics 1.4.0",
            "body": "New features release",
            "html_url": "https://github.com/duckarp/Caissalytics/releases/tag/v1.4.0",
            "published_at": "2026-09-08T18:00:00Z",
            "assets": [
                {
                    "name": "Caissalytics-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/duckarp/Caissalytics/releases/download/v1.4.0/Caissalytics-linux-x64.tar.gz",
                    "size": 50000000
                },
                {
                    "name": "Caissalytics-win-x64.zip",
                    "browser_download_url": "https://github.com/duckarp/Caissalytics/releases/download/v1.4.0/Caissalytics-win-x64.zip",
                    "size": 52000000
                }
            ]
        }
        """;

        var mockHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(fakeJson, System.Text.Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(httpClient);
        var update = await service.CheckForUpdatesAsync(force: true);

        Assert.NotNull(update);
        Assert.Equal("1.3.0", service.GetCurrentVersion());
        Assert.Equal("1.4.0", update.LatestVersion);
        Assert.True(update.IsUpdateAvailable);
        Assert.NotNull(update.AssetDownloadUrl);
        Assert.NotEmpty(update.AssetDownloadUrl!);
        Assert.NotNull(update.AssetFileName);
        Assert.NotEmpty(update.AssetFileName!);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_HandlesRateLimitOrNetworkFailure_Gracefully()
    {
        var mockHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
        using var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(httpClient);
        var update = await service.CheckForUpdatesAsync(force: true);

        Assert.NotNull(update);
        Assert.False(update.IsUpdateAvailable);
        Assert.Contains("Forbidden", update.StatusMessage);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _sender;
        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> sender) => _sender = sender;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_sender(request));
    }
}
