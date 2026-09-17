using Caissalytics.Core;

namespace Caissalytics.Engine;

public class PracticeEngineService : IPracticeEngineService
{
    private readonly IEngineService _engineService;
    private readonly IMaiaModelService? _maiaService;
    private UciProcessClient? _client;
    private UciProcessClient? _lc0Client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _currentLoadedEnginePath;
    private string? _currentLoadedLc0Path;
    private string? _currentLoadedWeightsPath;
    private bool _isDisposed;

    public PracticeEngineService(IEngineService engineService, IMaiaModelService? maiaService = null)
    {
        _engineService = engineService;
        _maiaService = maiaService;
    }

    public async Task<Move?> GetBotMoveAsync(string fen, int elo, PracticeBotType botType = PracticeBotType.Stockfish, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_isDisposed) return null;

            var pos = FenParser.Parse(fen);
            var legalMoves = MoveGenerator.GenerateLegalMoves(pos);
            if (legalMoves.Count == 0) return null;

            if (botType == PracticeBotType.Maia && _maiaService != null)
            {
                var maiaMove = await GetMaiaBotMoveAsync(fen, elo, legalMoves, ct);
                if (maiaMove.HasValue && !maiaMove.Value.IsEmpty)
                {
                    return maiaMove;
                }
                // Fallback to Stockfish if Maia was unavailable or unconfigured
            }

            bool clientReady = await EnsureClientAsync(ct);
            if (!clientReady || _client == null)
            {
                // Fallback to a legal move if engine cannot be initialized
                return PickFallbackMove(legalMoves);
            }

            // Configure engine strength based on Elo
            int clampedElo = Math.Clamp(elo, 800, 3190);
            int stockfishElo = Math.Clamp(clampedElo, 1320, 3190);
            int maxDepth = 0;
            int movetimeMs = 600;

            if (clampedElo < 1320)
            {
                // For sub-1320 ratings, limit search depth & skill level
                int skill = Math.Clamp((clampedElo - 800) / 100, 0, 8);
                maxDepth = clampedElo <= 1000 ? 3 : 5;
                movetimeMs = 350;

                await _client.SetOptionAsync("UCI_LimitStrength", "true");
                await _client.SetOptionAsync("UCI_Elo", "1320");
                await _client.SetOptionAsync("Skill Level", skill.ToString());
            }
            else if (clampedElo <= 2500)
            {
                await _client.SetOptionAsync("UCI_LimitStrength", "true");
                await _client.SetOptionAsync("UCI_Elo", stockfishElo.ToString());
                await _client.SetOptionAsync("Skill Level", "20");
            }
            else
            {
                await _client.SetOptionAsync("UCI_LimitStrength", "false");
                await _client.SetOptionAsync("Skill Level", "20");
                movetimeMs = 800;
            }

            int multiPv = clampedElo < 2200 ? 3 : 1;
            var (bestMoveUci, lines) = await _client.SearchPositionAsync(fen, movetimeMs, maxDepth, multiPv, ct);

            // Natural human thinking simulation delay
            int delayMs = Random.Shared.Next(350, 750);
            await Task.Delay(delayMs, ct);

            string? chosenUci = SelectMoveFromLines(lines, bestMoveUci, clampedElo);

            if (!string.IsNullOrEmpty(chosenUci))
            {
                var matched = legalMoves.FirstOrDefault(m => m.ToUci().Equals(chosenUci, StringComparison.OrdinalIgnoreCase));
                if (!matched.IsEmpty)
                {
                    return matched;
                }
            }

            // Fallback to top legal move
            return legalMoves[0];
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Move?> GetHintMoveAsync(string fen, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_isDisposed) return null;

            var pos = FenParser.Parse(fen);
            var legalMoves = MoveGenerator.GenerateLegalMoves(pos);
            if (legalMoves.Count == 0) return null;

            bool clientReady = await EnsureClientAsync(ct);
            if (!clientReady || _client == null)
            {
                return legalMoves[0];
            }

            // Search at full strength for coaching hint
            await _client.SetOptionAsync("UCI_LimitStrength", "false");
            await _client.SetOptionAsync("Skill Level", "20");

            var (bestMoveUci, lines) = await _client.SearchPositionAsync(fen, movetimeMs: 600, maxDepth: 12, multiPv: 1, ct);

            string? hintUci = bestMoveUci;
            if (string.IsNullOrEmpty(hintUci) && lines.Count > 0 && lines[0].PvMoves.Count > 0)
            {
                hintUci = lines[0].PvMoves[0];
            }

            if (!string.IsNullOrEmpty(hintUci))
            {
                var matched = legalMoves.FirstOrDefault(m => m.ToUci().Equals(hintUci, StringComparison.OrdinalIgnoreCase));
                if (!matched.IsEmpty)
                {
                    return matched;
                }
            }

            return legalMoves[0];
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EngineEvaluationLine?> GetEvaluationAsync(string fen, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_isDisposed) return null;

            bool clientReady = await EnsureClientAsync(ct);
            if (!clientReady || _client == null) return null;

            return await _client.EvaluatePositionAsync(fen, movetimeMs: 250, maxDepth: 10, ct);
        }
        catch
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_client != null && _client.IsRunning)
            {
                await _client.StopAnalysisAsync();
            }
            if (_lc0Client != null && _lc0Client.IsRunning)
            {
                await _lc0Client.StopAnalysisAsync();
            }
        }
        catch { }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Move?> GetMaiaBotMoveAsync(string fen, int elo, IReadOnlyList<Move> legalMoves, CancellationToken ct)
    {
        if (_maiaService == null) return null;

        var model = _maiaService.GetClosestModel(elo);
        if (model == null || !model.IsDownloaded || string.IsNullOrEmpty(model.FilePath))
        {
            return null;
        }

        string? lc0Path = _maiaService.FindLc0Executable();
        if (string.IsNullOrEmpty(lc0Path) || !File.Exists(lc0Path))
        {
            return null;
        }

        bool ready = await EnsureLc0ClientAsync(lc0Path, model.FilePath, ct);
        if (!ready || _lc0Client == null)
        {
            return null;
        }

        var (bestMoveUci, lines) = await _lc0Client.SearchPositionAsync(
            fen,
            movetimeMs: 0,
            maxDepth: 0,
            multiPv: 1,
            nodes: 1,
            ct: ct
        );

        int delayMs = Random.Shared.Next(400, 800);
        await Task.Delay(delayMs, ct);

        string? chosenUci = bestMoveUci;
        if (string.IsNullOrEmpty(chosenUci) && lines.Count > 0 && lines[0].PvMoves.Count > 0)
        {
            chosenUci = lines[0].PvMoves[0];
        }

        if (!string.IsNullOrEmpty(chosenUci))
        {
            var matched = legalMoves.FirstOrDefault(m => m.ToUci().Equals(chosenUci, StringComparison.OrdinalIgnoreCase));
            if (!matched.IsEmpty)
            {
                return matched;
            }
        }

        return null;
    }

    private async Task<bool> EnsureLc0ClientAsync(string lc0Path, string weightsPath, CancellationToken ct)
    {
        if (_lc0Client != null && _lc0Client.IsRunning && _currentLoadedLc0Path == lc0Path)
        {
            if (_currentLoadedWeightsPath != weightsPath)
            {
                await _lc0Client.SetOptionAsync("WeightsFile", weightsPath);
                _currentLoadedWeightsPath = weightsPath;
            }
            return true;
        }

        _lc0Client?.Dispose();
        _lc0Client = new UciProcessClient();
        bool started = await _lc0Client.StartEngineAsync(lc0Path);
        if (started)
        {
            _currentLoadedLc0Path = lc0Path;
            await _lc0Client.SetOptionAsync("WeightsFile", weightsPath);
            await _lc0Client.SetOptionAsync("MoveOverheadMs", "50");
            _currentLoadedWeightsPath = weightsPath;
            return true;
        }

        _lc0Client.Dispose();
        _lc0Client = null;
        return false;
    }

    private async Task<bool> EnsureClientAsync(CancellationToken ct)
    {
        var engine = await _engineService.GetActiveEngineAsync();
        if (engine == null || !engine.IsInstalled || string.IsNullOrEmpty(engine.ExecutablePath))
        {
            return false;
        }

        if (_client != null && _client.IsRunning && _currentLoadedEnginePath == engine.ExecutablePath)
        {
            return true;
        }

        // Restart client with active engine
        _client?.Dispose();
        _client = new UciProcessClient();
        bool started = await _client.StartEngineAsync(engine.ExecutablePath);
        if (started)
        {
            _currentLoadedEnginePath = engine.ExecutablePath;
            return true;
        }

        _client.Dispose();
        _client = null;
        return false;
    }

    private static string? SelectMoveFromLines(List<EngineEvaluationLine> lines, string? bestMoveUci, int elo)
    {
        var validCandidates = lines
            .Where(l => l.PvMoves.Count > 0 && !string.IsNullOrEmpty(l.PvMoves[0]))
            .OrderBy(l => l.MultiPvIndex)
            .ToList();

        if (validCandidates.Count == 0)
        {
            return bestMoveUci;
        }

        if (validCandidates.Count == 1 || elo >= 2200)
        {
            return validCandidates[0].PvMoves[0];
        }

        var top = validCandidates[0];

        // Never play a lower move if top move is a forced checkmate
        if (top.MateInMoves.HasValue && top.MateInMoves.Value > 0)
        {
            return top.PvMoves[0];
        }

        double topCp = top.Centipawns ?? 0;
        int roll = Random.Shared.Next(100);

        if (elo < 1400)
        {
            // Lower rating: 50% top move, 35% 2nd move, 15% 3rd move (if safe)
            if (validCandidates.Count >= 2 && roll >= 50 && roll < 85)
            {
                var cand2 = validCandidates[1];
                double delta = Math.Abs((cand2.Centipawns ?? 0) - topCp);
                if (delta <= 250 && (!cand2.MateInMoves.HasValue || cand2.MateInMoves.Value > 0))
                {
                    return cand2.PvMoves[0];
                }
            }
            else if (validCandidates.Count >= 3 && roll >= 85)
            {
                var cand3 = validCandidates[2];
                double delta = Math.Abs((cand3.Centipawns ?? 0) - topCp);
                if (delta <= 300 && (!cand3.MateInMoves.HasValue || cand3.MateInMoves.Value > 0))
                {
                    return cand3.PvMoves[0];
                }
            }
        }
        else if (elo < 1800)
        {
            // Intermediate: 70% top move, 25% 2nd move, 5% 3rd move
            if (validCandidates.Count >= 2 && roll >= 70 && roll < 95)
            {
                var cand2 = validCandidates[1];
                double delta = Math.Abs((cand2.Centipawns ?? 0) - topCp);
                if (delta <= 120)
                {
                    return cand2.PvMoves[0];
                }
            }
            else if (validCandidates.Count >= 3 && roll >= 95)
            {
                var cand3 = validCandidates[2];
                double delta = Math.Abs((cand3.Centipawns ?? 0) - topCp);
                if (delta <= 180)
                {
                    return cand3.PvMoves[0];
                }
            }
        }
        else
        {
            // Club player (1800-2190): 85% top move, 15% 2nd move
            if (validCandidates.Count >= 2 && roll >= 85)
            {
                var cand2 = validCandidates[1];
                double delta = Math.Abs((cand2.Centipawns ?? 0) - topCp);
                if (delta <= 60)
                {
                    return cand2.PvMoves[0];
                }
            }
        }

        return top.PvMoves[0];
    }

    private static Move PickFallbackMove(IReadOnlyList<Move> legal)
    {
        // Avoid picking pawn moves to edges or weird moves if possible
        return legal[0];
    }

    public void Dispose()
    {
        _isDisposed = true;
        _client?.Dispose();
        _client = null;
        _lc0Client?.Dispose();
        _lc0Client = null;
        _gate.Dispose();
    }
}
