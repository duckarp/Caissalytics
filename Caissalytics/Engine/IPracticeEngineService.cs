using Caissalytics.Core;

namespace Caissalytics.Engine;

public interface IPracticeEngineService : IDisposable
{
    Task<Move?> GetBotMoveAsync(string fen, int elo, CancellationToken ct = default);
    Task<Move?> GetHintMoveAsync(string fen, CancellationToken ct = default);
    Task<EngineEvaluationLine?> GetEvaluationAsync(string fen, CancellationToken ct = default);
    Task StopAsync();
}
