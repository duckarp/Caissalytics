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
            "tag_name": "v99.0.0",
            "name": "Caissalytics 99.0.0",
            "body": "New features release",
            "html_url": "https://github.com/duckarp/Caissalytics/releases/tag/v99.0.0",
            "published_at": "2026-09-08T18:00:00Z",
            "assets": [
                {
                    "name": "Caissalytics-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/duckarp/Caissalytics/releases/download/v99.0.0/Caissalytics-linux-x64.tar.gz",
                    "size": 50000000
                },
                {
                    "name": "Caissalytics-win-x64.zip",
                    "browser_download_url": "https://github.com/duckarp/Caissalytics/releases/download/v99.0.0/Caissalytics-win-x64.zip",
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

        // GetCurrentVersion() is read from the app assembly's InformationalVersion, so it tracks
        // the real release version. Don't pin it to a specific value (it changes every release) —
        // only check it is valid semver and older than the mocked latest (99.0.0), which drives
        // IsUpdateAvailable to true.
        string currentVer = service.GetCurrentVersion();
        Assert.True(Version.TryParse(currentVer, out _), $"GetCurrentVersion() '{currentVer}' is not valid semver");
        Assert.True(SemVerHelper.IsNewerVersion("99.0.0", currentVer), $"current version {currentVer} should be older than mocked latest 99.0.0");

        Assert.Equal("99.0.0", update.LatestVersion);
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

    [Theory]
    [InlineData("feat(analysis): add board position editor", "Feature", "Analysis", "Add board position editor")]
    [InlineData("fix: center modal button contents", "Fix", "", "Center modal button contents")]
    [InlineData("perf(engine): optimize move evaluation speed", "Performance", "Engine", "Optimize move evaluation speed")]
    [InlineData("style(dashboard): improve spacing between sections", "UI / Design", "Dashboard", "Improve spacing between sections")]
    [InlineData("refactor(tabs): streamline workspace tab switching", "Refactor", "Tabs", "Streamline workspace tab switching")]
    [InlineData("docs: update readme with changelog details", "Docs", "", "Update readme with changelog details")]
    [InlineData("improved keyboard navigation across cards", "Update", "", "Improved keyboard navigation across cards")]
    public void ParseCommitMessage_ParsesVariousCommitFormats(string message, string expectedCat, string expectedScope, string expectedDesc)
    {
        var item = UpdateService.ParseCommitMessage(message, "abc1234567", "duckarp");
        Assert.NotNull(item);
        Assert.Equal(expectedCat, item.Category);
        Assert.Equal(expectedScope, item.Scope);
        Assert.Equal(expectedDesc, item.Description);
        Assert.Equal("abc1234", item.CommitSha);
        Assert.Equal("duckarp", item.Author);
    }

    [Theory]
    [InlineData("Merge pull request #42 from duckarp/feature")]
    [InlineData("Merge branch 'main' of github.com:duckarp/Caissalytics")]
    [InlineData("Merge remote-tracking branch 'origin/main'")]
    [InlineData("chore: release v1.7.4")]
    [InlineData("chore(release): bump version to 1.7.4")]
    [InlineData("version bump 1.7.4")]
    [InlineData("bump version to 1.7.4")]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseCommitMessage_IgnoresMergeAndReleaseBumps(string message)
    {
        var item = UpdateService.ParseCommitMessage(message);
        Assert.Null(item);
        Assert.True(UpdateService.IsIgnoredCommit(message));
    }

    [Fact]
    public void SanitizeReleaseNotes_RemovesFullChangelogLink_RetainsOtherContent()
    {
        string raw = "Welcome to v1.7.4!\n\n**Full Changelog**: https://github.com/duckarp/Caissalytics/compare/v1.7.3...v1.7.4\nEnjoy playing chess!";
        string sanitized = UpdateService.SanitizeReleaseNotes(raw);

        Assert.DoesNotContain("**Full Changelog**", sanitized);
        Assert.DoesNotContain("compare/v1.7.3...v1.7.4", sanitized);
        Assert.Contains("Welcome to v1.7.4!", sanitized);
        Assert.Contains("Enjoy playing chess!", sanitized);
    }

    [Fact]
    public void SanitizeReleaseNotes_OnlyCompareLink_ReturnsEmpty()
    {
        string raw = "**Full Changelog**: https://github.com/duckarp/Caissalytics/compare/v1.7.3...v1.7.4";
        string sanitized = UpdateService.SanitizeReleaseNotes(raw);

        Assert.Equal(string.Empty, sanitized);
    }

    [Fact]
    public void ParseReleaseElement_ExtractsCompareUrl()
    {
        string json = """
        {
            "tag_name": "v1.7.4",
            "name": "Caissalytics 1.7.4",
            "body": "**Full Changelog**: https://github.com/duckarp/Caissalytics/compare/v1.7.3...v1.7.4",
            "html_url": "https://github.com/duckarp/Caissalytics/releases/tag/v1.7.4",
            "assets": []
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var updateInfo = new UpdateInfo();

        UpdateService.ParseReleaseElement(doc.RootElement, updateInfo, currentVer: "1.7.3");

        Assert.Equal("https://github.com/duckarp/Caissalytics/compare/v1.7.3...v1.7.4", updateInfo.CompareUrl);
        Assert.True(updateInfo.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_EnrichesChangelogFromCompareApi()
    {
        string releaseJson = """
        {
            "tag_name": "v99.0.0",
            "name": "v99.0.0",
            "body": "**Full Changelog**: https://github.com/duckarp/Caissalytics/compare/v1.7.3...v99.0.0",
            "html_url": "https://github.com/duckarp/Caissalytics/releases/tag/v99.0.0",
            "published_at": "2026-09-23T12:00:00Z",
            "assets": []
        }
        """;

        string compareJson = """
        {
            "commits": [
                {
                    "sha": "1234567890abcdef",
                    "commit": {
                        "message": "feat(board): add edit position capability\n\nDetailed explanation",
                        "author": { "name": "duckarp" }
                    }
                },
                {
                    "sha": "fedcba0987654321",
                    "commit": {
                        "message": "fix: vertically center button icons",
                        "author": { "name": "duckarp" }
                    }
                },
                {
                    "sha": "1111222233334444",
                    "commit": {
                        "message": "chore: release v99.0.0",
                        "author": { "name": "github-actions[bot]" }
                    }
                }
            ]
        }
        """;

        var mockHandler = new TestHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().Contains("/compare/"))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(compareJson, System.Text.Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(httpClient);
        var update = await service.CheckForUpdatesAsync(force: true);

        Assert.NotNull(update);
        Assert.Equal("https://github.com/duckarp/Caissalytics/compare/v1.7.3...v99.0.0", update.CompareUrl);
        Assert.Equal(2, update.ChangelogItems.Count);

        var first = update.ChangelogItems[0];
        Assert.Equal("Feature", first.Category);
        Assert.Equal("Board", first.Scope);
        Assert.Equal("Add edit position capability", first.Description);
        Assert.Equal("badge-feat", first.BadgeClass);

        var second = update.ChangelogItems[1];
        Assert.Equal("Fix", second.Category);
        Assert.Equal("Vertically center button icons", second.Description);
        Assert.Equal("badge-fix", second.BadgeClass);

        Assert.Contains("Add edit position capability", update.ReleaseNotes);
    }


    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _sender;
        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> sender) => _sender = sender;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_sender(request));
    }
}
