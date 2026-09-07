namespace Caissalytics.Engine;

public interface IEngineService
{
    Task<IReadOnlyList<EngineInfo>> GetEnginesAsync();
    Task<EngineInfo?> GetActiveEngineAsync();
    Task SetActiveEngineAsync(string engineId);
    Task<bool> InstallEngineAsync(string engineId, IProgress<int>? progress = null);
    Task SetCustomEnginePathAsync(string name, string path);

    Task StartAnalysisAsync(string fen, int multiPv, Action<List<EngineEvaluationLine>> onUpdate, CancellationToken ct = default);
    Task StopAnalysisAsync();
    bool IsAnalyzing { get; }
}
