namespace Caissalytics.Data;

public interface IAppearanceService
{
    Task<AppearanceSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppearanceSettings settings);
    Task PlaySoundAsync(ChessSoundType soundType);
    event Action<AppearanceSettings>? OnAppearanceChanged;
}
