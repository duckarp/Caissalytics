namespace Caissalytics.Data;

public interface IAppearanceService
{
    Task<AppearanceSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppearanceSettings settings);
    Task PlaySoundAsync(ChessSoundType soundType, Microsoft.JSInterop.IJSRuntime? jsRuntime = null);
    void SetJSRuntime(Microsoft.JSInterop.IJSRuntime jsRuntime);
    event Action<AppearanceSettings>? OnAppearanceChanged;
}
