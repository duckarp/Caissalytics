using Caissalytics.Core;

namespace Caissalytics.Engine;

public interface IGameAnalysisService
{
    Task<GameAnalysisReport> AnalyzeGameAsync(
        GameTree tree, 
        GameAnalysisOptions options, 
        IProgress<GameAnalysisProgress>? progress = null, 
        CancellationToken ct = default);

    void AnnotateGameTree(GameTree tree, GameAnalysisReport report);
}
