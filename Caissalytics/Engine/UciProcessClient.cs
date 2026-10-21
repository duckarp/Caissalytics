using System.Diagnostics;
using System.Text.RegularExpressions;
using Caissalytics.Core;

namespace Caissalytics.Engine;

public class UciProcessClient : IDisposable
{
    // One entry per "go" sent. UCI answers every "go" with exactly one "bestmove" (even after
    // "stop"), in order, so the head of this queue is always the search producing the current
    // output. This lets a late "bestmove" from a cancelled search be told apart from the answer
    // to the request that replaced it.
    private sealed record PendingSearch(int Id, PieceColor SideToMove);

    private Process? _process;
    private StreamWriter? _stdin;
    private readonly object _lock = new();
    private readonly Dictionary<int, EngineEvaluationLine> _currentLines = new();
    private Action<List<EngineEvaluationLine>>? _onUpdate;
    private TaskCompletionSource<EngineEvaluationLine?>? _evalTcs;
    private TaskCompletionSource<(string? BestMove, List<EngineEvaluationLine> Lines)>? _searchTcs;
    private int _lastDispatchedDepth = 0;
    private long _lastDispatchTimestamp = 0;
    private Timer? _throttleTimer;
    private bool _hasPendingDispatch = false;
    private TaskCompletionSource<bool>? _readyTcs;
    private TaskCompletionSource<bool>? _uciOkTcs;
    private TaskCompletionSource<bool>? _idleTcs;
    private readonly Queue<PendingSearch> _pendingSearches = new();
    private int _searchIdCounter = 0;
    private int _activeSearchId = 0; // search whose output is wanted; 0 = none
    private int _analysisSearchId = 0;
    // _commandGate serializes setting up searches; _writeGate serializes writes to stdin
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private const int StopTimeoutMs = 2000;

    public bool IsRunning
    {
        get
        {
            var process = _process;
            try
            {
                return process != null && !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public async Task<bool> StartEngineAsync(string executablePath)
    {
        Stop();

        if (!File.Exists(executablePath))
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            var uciOk = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _uciOkTcs = uciOk;
            }

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.Exited += OnProcessExited;
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _stdin = _process.StandardInput;

            await SendCommandAsync("uci");
            await Task.WhenAny(uciOk.Task, Task.Delay(5000));
            if (!IsRunning)
            {
                Stop();
                return false;
            }

            await WaitReadyAsync(3000);
            return IsRunning;
        }
        catch
        {
            Stop();
            return false;
        }
    }

    public async Task StartAnalysisAsync(string fen, int multiPv, Action<List<EngineEvaluationLine>> onUpdate, int maxDepth = 0)
    {
        if (!IsRunning || _stdin == null)
            return;

        int requestId;
        lock (_lock)
        {
            _analysisSearchId++;
            requestId = _analysisSearchId;
            _throttleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _hasPendingDispatch = false;
            _currentLines.Clear();
            _onUpdate = null;
            _activeSearchId = 0;
        }

        // Stop the current search right away so stale lines stop arriving while we wait for the gate
        await SendCommandAsync("stop");

        await _commandGate.WaitAsync();
        try
        {
            if (!IsRunning || _stdin == null)
                return;

            lock (_lock)
            {
                if (_analysisSearchId != requestId)
                    return;
            }

            await PrepareForNewSearchAsync();

            lock (_lock)
            {
                if (_analysisSearchId != requestId)
                    return;

                SupersedePendingRequestsLocked();
                _lastDispatchedDepth = 0;
                _lastDispatchTimestamp = 0;
                _onUpdate = onUpdate;
            }

            await SendCommandAsync($"setoption name MultiPV value {Math.Max(1, Math.Min(5, multiPv))}");
            await SendCommandAsync($"position fen {fen}");

            lock (_lock)
            {
                if (_analysisSearchId != requestId)
                {
                    _onUpdate = null;
                    return;
                }
                RegisterSearchLocked(SideToMoveFromFen(fen));
            }

            await SendCommandAsync(maxDepth > 0 ? $"go depth {maxDepth}" : "go infinite");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<EngineEvaluationLine?> EvaluatePositionAsync(string fen, int movetimeMs, int maxDepth, CancellationToken ct = default)
    {
        if (!IsRunning || _stdin == null)
            return null;

        var tcs = new TaskCompletionSource<EngineEvaluationLine?>(TaskCreationOptions.RunContinuationsAsynchronously);

        string goCommand = (maxDepth > 0 && movetimeMs > 0)
            ? $"go depth {maxDepth} movetime {movetimeMs}"
            : (movetimeMs > 0 ? $"go movetime {movetimeMs}" : $"go depth {maxDepth}");

        await _commandGate.WaitAsync(ct);
        try
        {
            await PrepareForNewSearchAsync();
            lock (_lock)
            {
                SupersedePendingRequestsLocked();
                _evalTcs = tcs;
            }

            await SendCommandAsync("setoption name MultiPV value 1");
            await SendCommandAsync($"position fen {fen}");
            lock (_lock)
            {
                RegisterSearchLocked(SideToMoveFromFen(fen));
            }
            await SendCommandAsync(goCommand);
        }
        finally
        {
            _commandGate.Release();
        }

        using var reg = ct.Register(() =>
        {
            tcs.TrySetCanceled(ct);
            _ = SendCommandAsync("stop");
        });

        try
        {
            return await tcs.Task;
        }
        finally
        {
            lock (_lock)
            {
                if (_evalTcs == tcs) _evalTcs = null;
            }
        }
    }

    public Task<(string? BestMove, List<EngineEvaluationLine> Lines)> SearchPositionAsync(
        string fen,
        int movetimeMs,
        int maxDepth,
        int multiPv,
        CancellationToken ct)
        => SearchPositionAsync(fen, movetimeMs, maxDepth, multiPv, nodes: 0, ct: ct);

    public async Task<(string? BestMove, List<EngineEvaluationLine> Lines)> SearchPositionAsync(
        string fen,
        int movetimeMs,
        int maxDepth,
        int multiPv = 1,
        int nodes = 0,
        CancellationToken ct = default)
    {
        if (!IsRunning || _stdin == null)
            return (null, new List<EngineEvaluationLine>());

        var tcs = new TaskCompletionSource<(string? BestMove, List<EngineEvaluationLine> Lines)>(TaskCreationOptions.RunContinuationsAsynchronously);

        string goCommand;
        if (nodes > 0)
        {
            goCommand = $"go nodes {nodes}";
        }
        else if (maxDepth > 0 && movetimeMs > 0)
        {
            goCommand = $"go depth {maxDepth} movetime {movetimeMs}";
        }
        else if (movetimeMs > 0)
        {
            goCommand = $"go movetime {movetimeMs}";
        }
        else
        {
            goCommand = $"go depth {maxDepth}";
        }

        await _commandGate.WaitAsync(ct);
        try
        {
            await PrepareForNewSearchAsync();
            lock (_lock)
            {
                SupersedePendingRequestsLocked();
                _searchTcs = tcs;
            }

            await SendCommandAsync($"setoption name MultiPV value {Math.Max(1, Math.Min(5, multiPv))}");
            await SendCommandAsync($"position fen {fen}");
            lock (_lock)
            {
                RegisterSearchLocked(SideToMoveFromFen(fen));
            }
            await SendCommandAsync(goCommand);
        }
        finally
        {
            _commandGate.Release();
        }

        using var reg = ct.Register(() =>
        {
            tcs.TrySetCanceled(ct);
            _ = SendCommandAsync("stop");
        });

        try
        {
            return await tcs.Task;
        }
        finally
        {
            lock (_lock)
            {
                if (_searchTcs == tcs) _searchTcs = null;
            }
        }
    }

    public async Task SetOptionAsync(string name, string value)
    {
        await SendCommandAsync($"setoption name {name} value {value}");
    }

    public async Task StopAnalysisAsync()
    {
        lock (_lock)
        {
            _analysisSearchId++;
            _throttleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            if (_hasPendingDispatch)
            {
                FlushPendingUpdateLocked();
            }
            _onUpdate = null;
            _currentLines.Clear();
            if (_evalTcs == null && _searchTcs == null)
            {
                _activeSearchId = 0;
            }
        }

        if (_stdin != null && IsRunning)
        {
            await SendCommandAsync("stop");
        }
    }

    public async Task<bool> WaitReadyAsync(int timeoutMs = 1000)
    {
        if (_stdin == null || !IsRunning) return false;

        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            _readyTcs?.TrySetResult(false);
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _readyTcs = tcs;
        }

        await SendCommandAsync("isready");

        using var cts = new CancellationTokenSource(timeoutMs);
        using var reg = cts.Token.Register(() => tcs.TrySetResult(false));

        try
        {
            return await tcs.Task;
        }
        finally
        {
            lock (_lock)
            {
                if (_readyTcs == tcs) _readyTcs = null;
            }
        }
    }

    /// <summary>Stops any running search and waits (bounded) for its "bestmove" so it cannot answer the next request.</summary>
    private async Task PrepareForNewSearchAsync()
    {
        Task idle;
        lock (_lock)
        {
            _activeSearchId = 0;
            if (_pendingSearches.Count == 0) return;
            _idleTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            idle = _idleTcs.Task;
        }

        await SendCommandAsync("stop");
        await Task.WhenAny(idle, Task.Delay(StopTimeoutMs));
    }

    /// <summary>Completes requests replaced by a new one, so their callers don't wait forever.</summary>
    private void SupersedePendingRequestsLocked()
    {
        _evalTcs?.TrySetResult(null);
        _evalTcs = null;
        _searchTcs?.TrySetResult((null, new List<EngineEvaluationLine>()));
        _searchTcs = null;
        _onUpdate = null;
    }

    private void RegisterSearchLocked(PieceColor sideToMove)
    {
        int id = ++_searchIdCounter;
        _pendingSearches.Enqueue(new PendingSearch(id, sideToMove));
        _activeSearchId = id;
        _currentLines.Clear();
    }

    private void FailPendingRequestsLocked()
    {
        _pendingSearches.Clear();
        _activeSearchId = 0;
        _idleTcs?.TrySetResult(true);
        _idleTcs = null;
        _uciOkTcs?.TrySetResult(false);
        _uciOkTcs = null;
        _readyTcs?.TrySetResult(false);
        SupersedePendingRequestsLocked();
    }

    private static PieceColor SideToMoveFromFen(string fen)
    {
        var parts = fen.Trim().Split(' ');
        return parts.Length > 1 && parts[1].Equals("b", StringComparison.OrdinalIgnoreCase)
            ? PieceColor.Black
            : PieceColor.White;
    }

    private async Task SendCommandAsync(string command)
    {
        var stdin = _stdin;
        if (stdin == null || !IsRunning) return;

        await _writeGate.WaitAsync();
        try
        {
            await stdin.WriteLineAsync(command);
            await stdin.FlushAsync();
        }
        catch
        {
            // Process may have exited
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(sender, _process)) return;
            FailPendingRequestsLocked();
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;

        string line = e.Data.Trim();
        if (line == "uciok")
        {
            lock (_lock)
            {
                _uciOkTcs?.TrySetResult(true);
                _uciOkTcs = null;
            }
            return;
        }

        if (line == "readyok")
        {
            lock (_lock)
            {
                _readyTcs?.TrySetResult(true);
            }
            return;
        }

        if (line.StartsWith("info ") && line.Contains(" pv "))
        {
            ParseInfoLine(line);
        }
        else if (line.StartsWith("bestmove"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? bm = parts.Length > 1 && parts[1] != "(none)" ? parts[1] : null;

            lock (_lock)
            {
                if (_pendingSearches.Count == 0) return;

                var finished = _pendingSearches.Dequeue();
                if (_pendingSearches.Count == 0)
                {
                    _idleTcs?.TrySetResult(true);
                    _idleTcs = null;
                }

                // A late answer to a search that was stopped and replaced
                if (finished.Id != _activeSearchId) return;

                if (_evalTcs != null)
                {
                    _currentLines.TryGetValue(1, out var top);
                    if (top == null && !string.IsNullOrEmpty(bm))
                    {
                        top = new EngineEvaluationLine
                        {
                            MultiPvIndex = 1,
                            PvMoves = new List<string> { bm }
                        };
                    }
                    _evalTcs.TrySetResult(top);
                    _evalTcs = null;
                }

                if (_searchTcs != null)
                {
                    var lines = _currentLines.Values.OrderBy(l => l.MultiPvIndex).ToList();
                    _searchTcs.TrySetResult((bm, lines));
                    _searchTcs = null;
                }
            }
        }
    }

    private void ParseInfoLine(string line)
    {
        int searchId;
        PieceColor sideToMove;
        lock (_lock)
        {
            if (_onUpdate == null && _evalTcs == null && _searchTcs == null)
                return;
            if (_pendingSearches.Count == 0 || _pendingSearches.Peek().Id != _activeSearchId)
                return; // output of a search nobody is waiting for any more
            searchId = _activeSearchId;
            sideToMove = _pendingSearches.Peek().SideToMove;
        }

        // Bound scores are provisional (fail-high/low) and would make the eval flicker
        if (line.Contains(" lowerbound") || line.Contains(" upperbound"))
            return;

        int multiPv = 1;
        var mpvMatch = Regex.Match(line, @"multipv\s+(\d+)");
        if (mpvMatch.Success)
            multiPv = int.Parse(mpvMatch.Groups[1].Value);

        int depth = 0;
        var depthMatch = Regex.Match(line, @"\bdepth\s+(\d+)");
        if (depthMatch.Success)
            depth = int.Parse(depthMatch.Groups[1].Value);

        int seldepth = 0;
        var selMatch = Regex.Match(line, @"seldepth\s+(\d+)");
        if (selMatch.Success)
            seldepth = int.Parse(selMatch.Groups[1].Value);

        long nodes = 0;
        var nodesMatch = Regex.Match(line, @"nodes\s+(\d+)");
        if (nodesMatch.Success)
            nodes = long.Parse(nodesMatch.Groups[1].Value);

        long nps = 0;
        var npsMatch = Regex.Match(line, @"nps\s+(\d+)");
        if (npsMatch.Success)
            nps = long.Parse(npsMatch.Groups[1].Value);

        double? cp = null;
        int? mate = null;

        var cpMatch = Regex.Match(line, @"score\s+cp\s+(-?\d+)");
        if (cpMatch.Success)
        {
            double val = double.Parse(cpMatch.Groups[1].Value);
            // In UCI, score is side-to-move relative. Normalize to White perspective:
            cp = sideToMove == PieceColor.White ? val : -val;
        }

        var mateMatch = Regex.Match(line, @"score\s+mate\s+(-?\d+)");
        if (mateMatch.Success)
        {
            int val = int.Parse(mateMatch.Groups[1].Value);
            mate = sideToMove == PieceColor.White ? val : -val;
        }

        var pvMoves = new List<string>();
        int pvIndex = line.IndexOf(" pv ", StringComparison.Ordinal);
        if (pvIndex != -1)
        {
            string pvPart = line.Substring(pvIndex + 4);
            pvMoves.AddRange(pvPart.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var evalLine = new EngineEvaluationLine
        {
            MultiPvIndex = multiPv,
            Depth = depth,
            SelectiveDepth = seldepth,
            Centipawns = cp,
            MateInMoves = mate,
            Nodes = nodes,
            Nps = nps,
            PvMoves = pvMoves
        };

        lock (_lock)
        {
            if (searchId != _activeSearchId || _pendingSearches.Count == 0 || _pendingSearches.Peek().Id != searchId)
                return;

            _currentLines[multiPv] = evalLine;

            if (_onUpdate != null)
            {
                int maxDepth = _currentLines.Values.Count > 0 ? _currentLines.Values.Max(l => l.Depth) : 0;
                long now = Stopwatch.GetTimestamp();
                double elapsedMs = _lastDispatchTimestamp == 0 ? 999.0 : (now - _lastDispatchTimestamp) * 1000.0 / Stopwatch.Frequency;

                // Dispatch immediately if:
                // 1. Engine reached a deeper search depth
                // 2. Or at least 75ms elapsed since the previous UI notification
                if (maxDepth > _lastDispatchedDepth || elapsedMs >= 75)
                {
                    _lastDispatchedDepth = maxDepth;
                    _lastDispatchTimestamp = now;
                    _hasPendingDispatch = false;
                    _throttleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                    var ordered = _currentLines.Values.OrderBy(l => l.MultiPvIndex).ToList();
                    _onUpdate.Invoke(ordered);
                }
                else if (!_hasPendingDispatch)
                {
                    _hasPendingDispatch = true;
                    int delay = Math.Clamp(75 - (int)elapsedMs, 10, 75);
                    if (_throttleTimer == null)
                    {
                        _throttleTimer = new Timer(_ => FlushPendingUpdate(), null, delay, Timeout.Infinite);
                    }
                    else
                    {
                        _throttleTimer.Change(delay, Timeout.Infinite);
                    }
                }
            }
        }
    }

    private void FlushPendingUpdate()
    {
        lock (_lock)
        {
            FlushPendingUpdateLocked();
        }
    }

    private void FlushPendingUpdateLocked()
    {
        if (!_hasPendingDispatch || _onUpdate == null) return;
        _hasPendingDispatch = false;
        _lastDispatchTimestamp = Stopwatch.GetTimestamp();
        _lastDispatchedDepth = _currentLines.Values.Count > 0 ? _currentLines.Values.Max(l => l.Depth) : 0;
        var ordered = _currentLines.Values.OrderBy(l => l.MultiPvIndex).ToList();
        _onUpdate.Invoke(ordered);
    }

    public void Stop()
    {
        lock (_lock)
        {
            _throttleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            if (_hasPendingDispatch)
            {
                FlushPendingUpdateLocked();
            }
        }

        var process = _process;
        var stdin = _stdin;

        try
        {
            if (stdin != null && IsRunning && _writeGate.Wait(500))
            {
                try
                {
                    stdin.WriteLine("quit");
                    stdin.Flush();
                }
                finally
                {
                    _writeGate.Release();
                }
            }
        }
        catch { }

        if (process != null)
        {
            // Late output or exit notifications from this process must not reach a restarted one
            process.OutputDataReceived -= OnOutputDataReceived;
            process.Exited -= OnProcessExited;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch { }
            process.Dispose();
        }

        lock (_lock)
        {
            _process = null;
            _stdin = null;
            FailPendingRequestsLocked();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _throttleTimer?.Dispose();
            _throttleTimer = null;
        }
        Stop();
    }
}
