using System.Text.Json;
using Caissalytics.Data;
using Caissalytics.Localization;
using Xunit;

namespace Caissalytics.Tests;

public class UpdateTests : IDisposable
{
    // Keeps the service's update_settings.json out of the real app data folder
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "caissalytics_update_tests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

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
    public void ParseReleaseElement_CapturesSha256Digest_AndSkipsUnsupportedAssets()
    {
        const string sha = "e6f661ece4b1451a5c746bba1d47a11916e1f647c5b4a2811d0fcdebada658cb";
        string json = $$"""
        {
            "tag_name": "v9.9.9",
            "assets": [
                { "name": "Caissalytics-x86_64.AppImage", "browser_download_url": "https://example.invalid/a.AppImage", "size": 1, "digest": "sha256:{{sha}}" },
                { "name": "Caissalytics-win-x64.exe", "browser_download_url": "https://example.invalid/a.exe", "size": 1, "digest": "sha256:{{sha}}" },
                { "name": "Caissalytics-linux-x64.tar.gz", "browser_download_url": "https://example.invalid/l.tar.gz", "size": 2, "digest": "sha256:{{sha.ToUpperInvariant()}}" },
                { "name": "Caissalytics-win-x64.zip", "browser_download_url": "https://example.invalid/w.zip", "size": 3, "digest": "sha256:{{sha}}" }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var info = new UpdateInfo();
        UpdateService.ParseReleaseElement(doc.RootElement, info, currentVer: "1.0.0");

        string expected = OperatingSystem.IsWindows() ? "Caissalytics-win-x64.zip" : "Caissalytics-linux-x64.tar.gz";
        Assert.Equal(expected, info.AssetFileName);
        Assert.Equal(sha, info.AssetSha256);
    }

    [Fact]
    public void ParseReleaseElement_OnlyUnsupportedAssets_LeavesNoDownload()
    {
        string json = """
        { "tag_name": "v9.9.9", "assets": [ { "name": "Caissalytics.AppImage", "browser_download_url": "https://example.invalid/a", "size": 1 } ] }
        """;

        using var doc = JsonDocument.Parse(json);
        var info = new UpdateInfo();
        UpdateService.ParseReleaseElement(doc.RootElement, info, currentVer: "1.0.0");

        Assert.Null(info.AssetDownloadUrl);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("md5:0123", null)]
    [InlineData("sha256:xyz", null)]
    [InlineData("sha256:E6F661ECE4B1451A5C746BBA1D47A11916E1F647C5B4A2811D0FCDEBADA658CB", "e6f661ece4b1451a5c746bba1d47a11916e1f647c5b4a2811d0fcdebada658cb")]
    public void ParseSha256Digest_AcceptsOnlyWellFormedSha256(string? digest, string? expected)
    {
        Assert.Equal(expected, UpdateService.ParseSha256Digest(digest));
    }

    [Fact]
    public void VerifyDownload_RequiresMatchingHashAndSize()
    {
        byte[] payload = "caissalytics-update"u8.ToArray();
        string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();
        var update = new UpdateInfo { AssetSha256 = sha, AssetSizeBytes = payload.Length };

        Assert.True(UpdateService.VerifyDownload(sha, payload.Length, update));
        Assert.False(UpdateService.VerifyDownload(new string('0', 64), payload.Length, update));
        Assert.False(UpdateService.VerifyDownload(sha, payload.Length + 1, update));
        Assert.False(UpdateService.VerifyDownload(sha, payload.Length, new UpdateInfo { AssetSizeBytes = payload.Length }));
    }

    [Fact]
    public async Task DownloadUpdateAsync_WithoutPublishedChecksum_RefusesBeforeDownloading()
    {
        bool requested = false;
        var handler = new TestHttpMessageHandler(_ => { requested = true; return new HttpResponseMessage(System.Net.HttpStatusCode.OK); });
        var service = new UpdateService(new HttpClient(handler), _dir);
        var update = new UpdateInfo { AssetDownloadUrl = "https://example.invalid/w.zip", AssetFileName = "w.zip" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadUpdateAsync(update));
        Assert.False(requested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnixApplyScript_OnlyReplacesTheAppWhenUnpackingSucceeds(bool validArchive)
    {
        if (OperatingSystem.IsWindows()) return;

        string root = Path.Combine(_dir, "apply test's dir"); // space and quote: exercises the shell quoting
        string appDir = Path.Combine(root, "app");
        string staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(staging);

        // The "app": writes a marker when (re)launched
        string exe = Path.Combine(appDir, "Caissalytics");
        string marker = Path.Combine(root, "launched");
        await File.WriteAllTextAsync(exe, "#!/bin/sh\necho old > \"$(dirname \"$0\")/../launched\"\n");
        File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string archive = Path.Combine(staging, "Caissalytics-linux-x64.tar.gz");
        if (validArchive)
        {
            string payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);
            await File.WriteAllTextAsync(Path.Combine(payload, "Caissalytics"), "#!/bin/sh\necho new > \"$(dirname \"$0\")/../launched\"\n");
            await using var gz = new System.IO.Compression.GZipStream(File.Create(archive), System.IO.Compression.CompressionLevel.Fastest);
            await System.Formats.Tar.TarFile.CreateFromDirectoryAsync(payload, gz, includeBaseDirectory: false);
        }
        else
        {
            await File.WriteAllTextAsync(archive, "truncated download");
        }

        // A process id that has already exited
        using var finished = System.Diagnostics.Process.Start("true")!;
        await finished.WaitForExitAsync();

        string script = Path.Combine(staging, "apply_update.sh");
        await File.WriteAllTextAsync(script, UpdateService.BuildUnixApplyScript(finished.Id, appDir, archive, exe));
        using (var sh = System.Diagnostics.Process.Start("/bin/sh", $"\"{script}\"")!)
        {
            await sh.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }

        for (int i = 0; i < 50 && !File.Exists(marker); i++) await Task.Delay(100);

        Assert.Equal(validArchive ? "new" : "old", (await File.ReadAllTextAsync(marker)).Trim());
        Assert.False(File.Exists(script));
        Assert.False(Directory.Exists(Path.Combine(staging, "extracted")));
        if (!validArchive)
        {
            Assert.Contains("keeping the current version", await File.ReadAllTextAsync(Path.Combine(staging, "apply_update.log")));
        }
    }

    [Fact]
    public void EmbeddedSigningKey_MatchesTheKeyFileUsedByTheReleaseWorkflow()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Caissalytics.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);

        string repoKey = File.ReadAllText(Path.Combine(dir.FullName, "release-signing-public-key.pem"));
        static string Normalize(string pem) => string.Concat(pem.Where(c => !char.IsWhiteSpace(c)));
        Assert.Equal(Normalize(repoKey), Normalize(UpdateService.ReleaseSigningPublicKeyPem));
    }

    [Fact]
    public void CurrentVersion_MatchesTheProjectVersion()
    {
        // Pinned Assembly/File/InformationalVersion lines once went stale while <Version> was bumped,
        // so 1.9.26 and 1.9.27 reported 1.9.25 and kept offering themselves as an update.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Caissalytics.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var csproj = System.Xml.Linq.XDocument.Load(Path.Combine(dir.FullName, "Caissalytics", "Caissalytics.csproj"));
        string projectVersion = csproj.Descendants("Version").Single().Value.Trim();

        Assert.Equal(projectVersion, new UpdateService(new HttpClient(), _dir).GetCurrentVersion());
    }

    [Fact]
    public void VerifyReleaseSignature_AcceptsOpensslSignatures_AndRejectsOthers()
    {
        if (OperatingSystem.IsWindows()) return; // uses the openssl CLI exactly like the release workflow

        string dir = Path.Combine(_dir, "sig");
        Directory.CreateDirectory(dir);
        string key = Path.Combine(dir, "key.pem"), pub = Path.Combine(dir, "pub.pem");
        string file = Path.Combine(dir, "Caissalytics-linux-x64.tar.gz"), sig = file + ".sig";
        File.WriteAllBytes(file, System.Security.Cryptography.RandomNumberGenerator.GetBytes(50_000));

        RunOpenssl($"ecparam -name prime256v1 -genkey -noout -out \"{key}\"");
        RunOpenssl($"ec -in \"{key}\" -pubout -out \"{pub}\"");
        RunOpenssl($"dgst -sha256 -sign \"{key}\" -out \"{sig}\" \"{file}\"");

        string publicKey = File.ReadAllText(pub);
        byte[] signature = File.ReadAllBytes(sig);
        Assert.True(UpdateService.VerifyReleaseSignature(file, signature, publicKey));

        // Signed by a different key (e.g. the real release key) or a modified file: rejected
        Assert.False(UpdateService.VerifyReleaseSignature(file, signature, UpdateService.ReleaseSigningPublicKeyPem));
        File.AppendAllText(file, "x");
        Assert.False(UpdateService.VerifyReleaseSignature(file, signature, publicKey));
        Assert.False(UpdateService.VerifyReleaseSignature(file, [1, 2, 3], publicKey));
    }

    private static void RunOpenssl(string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("openssl", args) { RedirectStandardError = true })!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"openssl {args} failed: {p.StandardError.ReadToEnd()}");
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-key")]
    [InlineData("missing")]
    public async Task DownloadUpdateAsync_RequiresAValidReleaseSignature(string signatureCase)
    {
        byte[] package = System.Security.Cryptography.RandomNumberGenerator.GetBytes(10_000);
        string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)).ToLowerInvariant();

        using var releaseKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var otherKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var signer = signatureCase == "wrong-key" ? otherKey : releaseKey;
        byte[] signature = signer.SignData(package, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);

        string sigAsset = signatureCase == "missing" ? "" : """
            , { "name": "Caissalytics-linux-x64.tar.gz.sig", "browser_download_url": "https://example.invalid/pkg.sig", "size": 72 },
              { "name": "Caissalytics-win-x64.zip.sig", "browser_download_url": "https://example.invalid/pkg.sig", "size": 72 }
            """;
        string release = $$"""
            { "tag_name": "v99.0.0", "assets": [
              { "name": "Caissalytics-linux-x64.tar.gz", "browser_download_url": "https://example.invalid/pkg", "size": {{package.Length}}, "digest": "sha256:{{sha}}" },
              { "name": "Caissalytics-win-x64.zip", "browser_download_url": "https://example.invalid/pkg", "size": {{package.Length}}, "digest": "sha256:{{sha}}" }
              {{sigAsset}}
            ] }
            """;
        using var doc = JsonDocument.Parse(release);
        var update = new UpdateInfo();
        UpdateService.ParseReleaseElement(doc.RootElement, update, currentVer: "1.0.0");

        var handler = new TestHttpMessageHandler(req => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(req.RequestUri!.AbsolutePath.EndsWith(".sig") ? signature : package)
        });
        var service = new UpdateService(new HttpClient(handler), _dir, releaseKey.ExportSubjectPublicKeyInfoPem());

        switch (signatureCase)
        {
            case "valid":
                await service.DownloadUpdateAsync(update);
                break;
            case "wrong-key":
                await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadUpdateAsync(update));
                Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "update_staging")));
                break;
            case "missing":
                Assert.Null(update.SignatureDownloadUrl);
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadUpdateAsync(update));
                break;
        }
    }

    [Fact]
    public void PrepareUpdateStagingDirectory_RecreatesEmptyOwnerOnlyFolder()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "caissalytics_update_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            string first = UpdateService.PrepareUpdateStagingDirectory(baseDir);
            File.WriteAllText(Path.Combine(first, "apply_update.sh"), "stale");

            string second = UpdateService.PrepareUpdateStagingDirectory(baseDir);

            Assert.Equal(first, second);
            Assert.StartsWith(baseDir, second);
            Assert.Empty(Directory.EnumerateFileSystemEntries(second));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(second));
            }
        }
        finally
        {
            try { Directory.Delete(baseDir, true); } catch { }
        }
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
        var service = new UpdateService(httpClient, _dir);
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
        var service = new UpdateService(httpClient, _dir);
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
        var service = new UpdateService(httpClient, _dir);
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


    [Theory]
    [InlineData("en", "Nav_CheckUpdate_Tooltip", "Click to check for updates")]
    [InlineData("en", "Nav_CheckingUpdates", "Checking for updates...")]
    [InlineData("en", "Nav_UpToDate", "Up to date")]
    [InlineData("en", "Nav_UpToDate_Tooltip", "Caissalytics is up to date")]
    [InlineData("sk", "Nav_CheckUpdate_Tooltip", "Kliknite pre kontrolu aktualizácií")]
    [InlineData("sk", "Nav_CheckingUpdates", "Kontrola aktualizácií...")]
    [InlineData("sk", "Nav_UpToDate", "Aktuálna verzia")]
    [InlineData("sk", "Nav_UpToDate_Tooltip", "Caissalytics je aktuálny")]
    public async Task Localization_ContainsVersionCheckKeys(string lang, string key, string expected)
    {
        var loc = new LocalizationService();
        await loc.SetLanguageAsync(lang);
        Assert.Equal(expected, loc[key]);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _sender;
        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> sender) => _sender = sender;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_sender(request));
    }
}
