using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class AppearanceTests
{
    [Fact]
    public void AppearanceSettings_Defaults_AreValid()
    {
        var settings = new AppearanceSettings();

        Assert.Equal("brown", settings.BoardTheme);
        Assert.Equal("cburnett", settings.PieceSet);
        Assert.True(settings.SoundEnabled);
        Assert.Equal(80, settings.Volume);
        Assert.True(settings.PlayMoveSound);
        Assert.True(settings.PlayCaptureSound);
        Assert.True(settings.PlayCheckSound);
        Assert.True(settings.PlayVictorySound);
        Assert.True(settings.PlayLowTimeSound);

        Assert.Contains("brown", AppearanceSettings.AvailableBoardThemes);
        Assert.Contains("green", AppearanceSettings.AvailableBoardThemes);
        Assert.Contains("blue", AppearanceSettings.AvailableBoardThemes);
        Assert.Contains("slate", AppearanceSettings.AvailableBoardThemes);
        Assert.Contains("marble", AppearanceSettings.AvailableBoardThemes);
        Assert.Contains("monochrome", AppearanceSettings.AvailableBoardThemes);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void AppearanceSettings_Volume_IsClamped(int input, int expected)
    {
        var settings = new AppearanceSettings
        {
            Volume = input
        };

        Assert.Equal(expected, settings.Volume);
    }

    [Fact]
    public async Task AppearanceService_SaveAndLoad_RoundtripsCleanly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_appearance_test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "appearance_settings.json");

        try
        {
            var service = new AppearanceService(settingsPath);

            // Initial load from non-existent file returns defaults
            var initial = await service.GetSettingsAsync();
            Assert.Equal("brown", initial.BoardTheme);
            Assert.Equal(80, initial.Volume);

            // Save modified settings
            var modified = new AppearanceSettings
            {
                BoardTheme = "green",
                Volume = 65,
                PlayCheckSound = false,
                SoundEnabled = true
            };
            await service.SaveSettingsAsync(modified);

            // Reload from fresh instance
            var freshService = new AppearanceService(settingsPath);
            var loaded = await freshService.GetSettingsAsync();

            Assert.Equal("green", loaded.BoardTheme);
            Assert.Equal(65, loaded.Volume);
            Assert.False(loaded.PlayCheckSound);
            Assert.True(loaded.PlayMoveSound);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AppearanceService_OnAppearanceChanged_FiresOnSave()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_appearance_test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "appearance_settings.json");

        try
        {
            var service = new AppearanceService(settingsPath);
            AppearanceSettings? received = null;
            service.OnAppearanceChanged += s => received = s;

            var toSave = new AppearanceSettings { BoardTheme = "slate", Volume = 90 };
            await service.SaveSettingsAsync(toSave);

            Assert.NotNull(received);
            Assert.Equal("slate", received!.BoardTheme);
            Assert.Equal(90, received.Volume);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AppearanceService_PlaySoundAsync_WithoutJsRuntime_DoesNotThrow()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_appearance_test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "appearance_settings.json");

        try
        {
            // Null JS runtime (desktop test environment)
            var service = new AppearanceService(settingsPath, jsRuntime: null);

            // Should complete cleanly without throwing
            await service.PlaySoundAsync(ChessSoundType.Move);
            await service.PlaySoundAsync(ChessSoundType.Capture);
            await service.PlaySoundAsync(ChessSoundType.Check);
            await service.PlaySoundAsync(ChessSoundType.Victory);
            await service.PlaySoundAsync(ChessSoundType.LowTime);

            // Muted should also complete cleanly
            await service.SaveSettingsAsync(new AppearanceSettings { SoundEnabled = false });
            await service.PlaySoundAsync(ChessSoundType.Move);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
