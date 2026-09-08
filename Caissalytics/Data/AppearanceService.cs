using System.Text.Json;
using Microsoft.JSInterop;

namespace Caissalytics.Data;

public class AppearanceService : IAppearanceService
{
    private IJSRuntime? _jsRuntime;
    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AppearanceSettings? _cachedSettings;

    public event Action<AppearanceSettings>? OnAppearanceChanged;

    public AppearanceService(IJSRuntime? jsRuntime = null)
    {
        _jsRuntime = jsRuntime;
        string configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics");
        Directory.CreateDirectory(configDir);
        _settingsFilePath = Path.Combine(configDir, "appearance_settings.json");
    }

    public AppearanceService(string customFilePath, IJSRuntime? jsRuntime = null)
    {
        _jsRuntime = jsRuntime;
        _settingsFilePath = customFilePath;
        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public void SetJSRuntime(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<AppearanceSettings> GetSettingsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            if (File.Exists(_settingsFilePath))
            {
                string json = await File.ReadAllTextAsync(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppearanceSettings>(json);
                if (settings != null)
                {
                    _cachedSettings = settings;
                    return _cachedSettings;
                }
            }

            _cachedSettings = new AppearanceSettings();
            return _cachedSettings;
        }
        catch
        {
            _cachedSettings = new AppearanceSettings();
            return _cachedSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppearanceSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _cachedSettings = settings;
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }
        finally
        {
            _lock.Release();
        }

        OnAppearanceChanged?.Invoke(settings);
    }

    public async Task PlaySoundAsync(ChessSoundType soundType, IJSRuntime? jsRuntime = null)
    {
        var settings = await GetSettingsAsync();

        if (!settings.SoundEnabled || settings.Volume <= 0)
        {
            return;
        }

        bool shouldPlay = soundType switch
        {
            ChessSoundType.Move => settings.PlayMoveSound,
            ChessSoundType.Capture => settings.PlayCaptureSound,
            ChessSoundType.Check => settings.PlayCheckSound,
            ChessSoundType.Victory => settings.PlayVictorySound,
            ChessSoundType.LowTime => settings.PlayLowTimeSound,
            _ => true
        };

        if (!shouldPlay)
        {
            return;
        }

        float volumeRatio = Math.Clamp(settings.Volume / 100f, 0f, 1f);

        // 1. Primary: Native system audio (zero latency, immune to WebKitGTK / browser audio sink issues)
        if (NativeAudioPlayer.Play(soundType, volumeRatio))
        {
            return;
        }

        // 2. Fallback: JavaScript audio playback (for Web / WebAssembly or if no native CLI player is found)
        var js = jsRuntime ?? _jsRuntime;
        if (js == null)
        {
            return;
        }

        string jsMethod = soundType switch
        {
            ChessSoundType.Move => "chessSound.playMove",
            ChessSoundType.Capture => "chessSound.playCapture",
            ChessSoundType.Check => "chessSound.playCheck",
            ChessSoundType.Victory => "chessSound.playVictory",
            ChessSoundType.LowTime => "chessSound.playLowTime",
            _ => "chessSound.playMove"
        };

        try
        {
            await js.InvokeVoidAsync(jsMethod, volumeRatio);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AppearanceService] JS PlaySound error: {ex.Message}");
        }
    }
}
