namespace Caissalytics.Data;

public interface IChessClubService
{
    event Action? OnSettingsChanged;
    Task<ChessClubSettings> GetSettingsAsync();
    Task SaveSettingsAsync(ChessClubSettings settings);
}
