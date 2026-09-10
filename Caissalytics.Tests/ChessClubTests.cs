using System;
using System.IO;
using System.Threading.Tasks;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class ChessClubTests : IDisposable
{
    private readonly string _testDir;

    public ChessClubTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "caissalytics_club_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task ChessClubService_ReturnsDefaults_WhenNoExistingFile()
    {
        var service = new ChessClubService(_testDir);
        var settings = await service.GetSettingsAsync();

        Assert.NotNull(settings);
        Assert.Equal(string.Empty, settings.ClubName);
        Assert.Equal(string.Empty, settings.WebsiteUrl);
    }

    [Fact]
    public async Task ChessClubService_SavesAndReloadsSettings()
    {
        var service = new ChessClubService(_testDir);
        await service.SaveSettingsAsync(new ChessClubSettings
        {
            ClubName = "Bratislava Chess Academy",
            WebsiteUrl = "https://chess-academy.example.com"
        });

        // New service instance pointing to the same storage
        var reloadedService = new ChessClubService(_testDir);
        var settings = await reloadedService.GetSettingsAsync();

        Assert.NotNull(settings);
        Assert.Equal("Bratislava Chess Academy", settings.ClubName);
        Assert.Equal("https://chess-academy.example.com", settings.WebsiteUrl);
    }

    [Fact]
    public async Task ChessClubService_FiresOnSettingsChangedEvent()
    {
        var service = new ChessClubService(_testDir);
        bool eventFired = false;
        service.OnSettingsChanged += () => eventFired = true;

        await service.SaveSettingsAsync(new ChessClubSettings
        {
            ClubName = "Kings & Queens Club"
        });

        Assert.True(eventFired);
    }
}
