using System.Diagnostics;
using System.Text.RegularExpressions;
using Caissalytics.Core;

namespace Caissalytics.Engine;

public class UciProcessClient : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private readonly object _lock = new();
    private readonly Dictionary<int, EngineEvaluationLine> _currentLines = new();
    private Action<List<EngineEvaluationLine>>? _onUpdate;
    private PieceColor _sideToMove = PieceColor.White;
    private TaskCompletionSource<EngineEvaluationLine?>? _evalTcs;

    public bool IsRunning => _process != null && !_process.HasExited;

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
            _process = new Process { StartInfo = psi };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _stdin = _process.StandardInput;

            await SendCommandAsync("uci");
            await Task.Delay(200);
            await SendCommandAsync("isready");

            return true;
        }
        catch
        {
            Stop();
            return false;
        }
    }

    public async Task StartAnalysisAsync(string fen, int multiPv, Action<List<EngineEvaluationLine>> onUpdate)
    {
        if (_process == null || _process.HasExited || _stdin == null)
            return;

        _onUpdate = onUpdate;
        lock (_lock)
        {
            _currentLines.Clear();
        }

        // Determine side to move from FEN for score normalization
        var parts = fen.Trim().Split(' ');
        _sideToMove = parts.Length > 1 && parts[1].Equals("b", StringComparison.OrdinalIgnoreCase)
            ? PieceColor.Black
            : PieceColor.White;

        await SendCommandAsync("stop");
        await SendCommandAsync($"setoption name MultiPV value {Math.Max(1, Math.Min(5, multiPv))}");
        await SendCommandAsync($"position fen {fen}");
        await SendCommandAsync("go infinite");
    }

    public async Task<EngineEvaluationLine?> EvaluatePositionAsync(string fen, int movetimeMs, int maxDepth, CancellationToken ct = default)
    {
        if (_process == null || _process.HasExited || _stdin == null)
            return null;

        var tcs = new TaskCompletionSource<EngineEvaluationLine?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var reg = ct.Register(() =>
        {
            _ = SendCommandAsync("stop");
            tcs.TrySetCanceled(ct);
        });

        lock (_lock)
        {
            _currentLines.Clear();
            _evalTcs = tcs;
        }

        var parts = fen.Trim().Split(' ');
        _sideToMove = parts.Length > 1 && parts[1].Equals("b", StringComparison.OrdinalIgnoreCase)
            ? PieceColor.Black
            : PieceColor.White;

        await SendCommandAsync("stop");
        await SendCommandAsync("setoption name MultiPV value 1");
        await SendCommandAsync($"position fen {fen}");

        string goCommand = (maxDepth > 0 && movetimeMs > 0)
            ? $"go depth {maxDepth} movetime {movetimeMs}"
            : (movetimeMs > 0 ? $"go movetime {movetimeMs}" : $"go depth {maxDepth}");

        await SendCommandAsync(goCommand);

        try
        {
            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            await SendCommandAsync("stop");
            throw;
        }
        finally
        {
            lock (_lock)
            {
                if (_evalTcs == tcs) _evalTcs = null;
            }
        }
    }

    public async Task SetOptionAsync(string name, string value)
    {
        await SendCommandAsync($"setoption name {name} value {value}");
    }

    public async Task StopAnalysisAsync()
    {
        if (_stdin != null && IsRunning)
        {
            await SendCommandAsync("stop");
        }
    }

    private async Task SendCommandAsync(string command)
    {
        if (_stdin != null && IsRunning)
        {
            try
            {
                await _stdin.WriteLineAsync(command);
                await _stdin.FlushAsync();
            }
            catch
            {
                // Process may have exited
            }
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;

        string line = e.Data.Trim();
        if (line.StartsWith("info ") && line.Contains(" pv "))
        {
            ParseInfoLine(line);
        }
        else if (line.StartsWith("bestmove"))
        {
            lock (_lock)
            {
                if (_evalTcs != null)
                {
                    _currentLines.TryGetValue(1, out var top);
                    _evalTcs.TrySetResult(top);
                    _evalTcs = null;
                }
            }
        }
    }

    private void ParseInfoLine(string line)
    {
        int multiPv = 1;
        var mpvMatch = Regex.Match(line, @"multipv\s+(\d+)");
        if (mpvMatch.Success)
            multiPv = int.Parse(mpvMatch.Groups[1].Value);

        int depth = 0;
        var depthMatch = Regex.Match(line, @"depth\s+(\d+)");
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
            cp = _sideToMove == PieceColor.White ? val : -val;
        }

        var mateMatch = Regex.Match(line, @"score\s+mate\s+(-?\d+)");
        if (mateMatch.Success)
        {
            int val = int.Parse(mateMatch.Groups[1].Value);
            mate = _sideToMove == PieceColor.White ? val : -val;
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
            _currentLines[multiPv] = evalLine;
            var ordered = _currentLines.Values.OrderBy(l => l.MultiPvIndex).ToList();
            _onUpdate?.Invoke(ordered);
        }
    }

    public void Stop()
    {
        try
        {
            if (_stdin != null && IsRunning)
            {
                _stdin.WriteLine("quit");
                _stdin.Flush();
            }
        }
        catch { }

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
            }
        }
        catch { }

        _process?.Dispose();
        _process = null;
        _stdin = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
