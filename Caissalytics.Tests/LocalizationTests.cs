using Caissalytics.Data;
using Caissalytics.Localization;
using Xunit;

namespace Caissalytics.Tests;

public class LocalizationTests
{
    [Fact]
    public void DefaultLanguage_IsEnglish_AndReturnsEnglishStrings()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);

        Assert.Equal("en", loc.CurrentLanguage);
        Assert.Equal("New Analysis Board", loc["Dash_Card_Analysis"]);
        Assert.Equal("Settings & Control Center", loc["Settings_Title"]);
        Assert.Equal("Save Settings", loc["Common_Save"]);
    }

    [Fact]
    public async Task SetLanguageAsync_SwitchesToSlovak_AndReturnsSlovakStrings()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);
        bool eventFired = false;
        loc.OnLanguageChanged += () => eventFired = true;

        await loc.SetLanguageAsync("sk");

        Assert.True(eventFired);
        Assert.Equal("sk", loc.CurrentLanguage);
        Assert.Equal("Nová analýza partie", loc["Dash_Card_Analysis"]);
        Assert.Equal("Nastavenia & Riadiace centrum", loc["Settings_Title"]);
        Assert.Equal("Uložiť nastavenia", loc["Common_Save"]);
        Assert.Equal("Odstrániť", loc["Common_Remove"]);
    }

    [Fact]
    public async Task FallbackToEnglish_WhenKeyMissingInSlovak()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);
        // Register a temporary key only in English
        loc.RegisterLanguage(new LanguageInfo("en", "English", "English", "🇬🇧"), new Dictionary<string, string>
        {
            ["Custom_English_Only"] = "Special English Content"
        });

        await loc.SetLanguageAsync("sk");

        // Key should fall back to English value
        Assert.Equal("Special English Content", loc["Custom_English_Only"]);
    }

    [Fact]
    public void FallbackToKey_WhenKeyCompletelyUnknown()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);

        Assert.Equal("Totally_Unknown_Key_123", loc["Totally_Unknown_Key_123"]);
    }

    [Fact]
    public async Task FormattedIndexer_SubstitutesParameters()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);

        Assert.Equal("Update v2.0.0", loc["Nav_Update", "2.0.0"]);

        await loc.SetLanguageAsync("sk");
        Assert.Equal("Aktualizácia v2.0.0", loc["Nav_Update", "2.0.0"]);
    }

    [Fact]
    public async Task RegisterLanguage_AllowsAddingNewLanguagesDynamically()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);

        var german = new LanguageInfo("de", "German", "Deutsch", "🇩🇪");
        loc.RegisterLanguage(german, new Dictionary<string, string>
        {
            ["Nav_Brand"] = "Caissalytics",
            ["Common_Save"] = "Einstellungen speichern",
            ["Dash_Card_Analysis"] = "Neues Analysebrett"
        });

        Assert.Contains(loc.SupportedLanguages, l => l.Code == "de");

        await loc.SetLanguageAsync("de");
        Assert.Equal("de", loc.CurrentLanguage);
        Assert.Equal("Einstellungen speichern", loc["Common_Save"]);
        Assert.Equal("Neues Analysebrett", loc["Dash_Card_Analysis"]);

        // Keys not translated into German fall back to English
        Assert.Equal("Browse Databases", loc["Dash_Card_Databases"]);
    }

    [Fact]
    public async Task SettingsPersistence_LoadsLanguagePreference()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_loc_test_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "appearance_settings.json");

        try
        {
            var appearance = new AppearanceService(settingsPath);
            await appearance.SaveSettingsAsync(new AppearanceSettings { Language = "sk" });

            var loc = new LocalizationService(appearance);
            Assert.Equal("sk", loc.CurrentLanguage);
            Assert.Equal("Nová analýza partie", loc["Dash_Card_Analysis"]);

            // Change through loc and verify persistence
            await loc.SetLanguageAsync("en");
            var savedSettings = await appearance.GetSettingsAsync();
            Assert.Equal("en", savedSettings.Language);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Homework_And_BoardEditor_KeysExistInBothLanguages()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);

        // English verification
        Assert.Equal("Homework & Diagram Sheets", loc["Homework_Title"]);
        Assert.Equal("Board Editor", loc["BoardEditor_Title"]);
        Assert.Equal("Starting Position", loc["BoardEditor_StartingPos"]);
        Assert.Equal("Move Up", loc["Common_MoveUp"]);

        // Slovak verification
        await loc.SetLanguageAsync("sk");
        Assert.Equal("Pracovné listy a diagramy", loc["Homework_Title"]);
        Assert.Equal("Editor šachovnice", loc["BoardEditor_Title"]);
        Assert.Equal("Základné postavenie", loc["BoardEditor_StartingPos"]);
        Assert.Equal("Posunúť nahor", loc["Common_MoveUp"]);
    }
}
