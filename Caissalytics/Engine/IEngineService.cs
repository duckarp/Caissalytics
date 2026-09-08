namespace Caissalytics.Engine;

public interface IEngineService
{
    Task<IReadOnlyList<EngineInfo>> GetEnginesAsync();
    Task<EngineInfo?> GetActiveEngineAsync();
    Task SetActiveEngineAsync(string engineId);
    Task<bool> InstallEngineAsync(string engineId, IProgress<int>? progress = null);
    Task<EngineProbeResult> ProbeEngineFileAsync(string executablePath);
    Task<bool> AddCustomEngineAsync(string name, string executablePath);
    Task<bool> RemoveEngineAsync(string engineId);
    Task<IReadOnlyList<EngineInfo>> ScanSystemEnginesAsync();
    Task SetCustomEnginePathAsync(string name, string path);

    Task StartAnalysisAsync(string fen, int multiPv, Action<List<EngineEvaluationLine>> onUpdate, CancellationToken ct = default);
    Task StopAnalysisAsync();
    bool IsAnalyzing { get; }
    string? SyzygyPath { get; }
    Task SetSyzygyPathAsync(string? path);

    Task<StockfishUpdateInfo> CheckStockfishUpdateAsync(bool force = false, CancellationToken ct = default);
    Task<bool> UpdateStockfishAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    StockfishUpdateInfo? CachedStockfishUpdate { get; }
    event Action<StockfishUpdateInfo>? OnStockfishUpdateChanged;

    event Action? OnEnginesChanged;
}
