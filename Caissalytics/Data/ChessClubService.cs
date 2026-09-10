using System.Text.Json;

namespace Caissalytics.Data;

public class ChessClubService : IChessClubService
{
    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ChessClubSettings? _cachedSettings;

    public event Action? OnSettingsChanged;

    public ChessClubService(string? storageDirectory = null)
    {
        string configDir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics");
        Directory.CreateDirectory(configDir);
        _settingsFilePath = Path.Combine(configDir, "chess_club_settings.json");
    }

    public ChessClubService(string customFilePath, bool isExplicitFilePath)
    {
        _settingsFilePath = customFilePath;
        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task<ChessClubSettings> GetSettingsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cachedSettings != null)
            {
                return CloneSettings(_cachedSettings);
            }

            if (File.Exists(_settingsFilePath))
            {
                string json = await File.ReadAllTextAsync(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<ChessClubSettings>(json);
                if (settings != null)
                {
                    _cachedSettings = settings;
                    return CloneSettings(_cachedSettings);
                }
            }

            _cachedSettings = new ChessClubSettings();
            return CloneSettings(_cachedSettings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChessClubService] Error reading settings: {ex.Message}");
            return new ChessClubSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(ChessClubSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _cachedSettings = CloneSettings(settings);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }
        finally
        {
            _lock.Release();
        }

        OnSettingsChanged?.Invoke();
    }

    private static ChessClubSettings CloneSettings(ChessClubSettings s) => new()
    {
        ClubName = s.ClubName ?? "",
        WebsiteUrl = s.WebsiteUrl ?? ""
    };
}
