namespace Caissalytics.Data;

public interface IRepertoireService
{
    event Action? OnRepertoireChanged;

    Task<RepertoireTree> GetRepertoireAsync();
    Task AddOrUpdateMoveAsync(RepertoireMove move);
    Task RemoveMoveAsync(string fen, string moveSan, string color);
    Task<List<RepertoireMove>> GetMovesForPositionAsync(string fen, string? color = null);
    Task<string> ExportRepertoireToPgnAsync(string color);
    string NormalizeFen(string fen);
    Task<List<RepertoireLine>> GetLinesAsync(string? color = null);
    Task SaveLineAsync(RepertoireLine line);
    Task DeleteLineAsync(string lineId);
    Task<RepertoireLine?> GetLineByIdAsync(string lineId);
}
