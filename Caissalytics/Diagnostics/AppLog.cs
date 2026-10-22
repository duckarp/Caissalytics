using System.Text;
using Microsoft.Extensions.Logging;

namespace Caissalytics.Diagnostics;

/// <summary>
/// File log for a desktop app that has no visible console (WinExe on Windows): everything written to
/// Console.Out/Console.Error is also appended, timestamped, to logs/caissalytics.log in the app data folder.
/// </summary>
public static class AppLog
{
    public const long MaxLogBytes = 2 * 1024 * 1024;

    public static string? LogFilePath { get; private set; }

    public static void Initialize(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            string path = Path.Combine(logDirectory, "caissalytics.log");
            RotateIfLarge(path, MaxLogBytes);

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(new LogTeeWriter(Console.Out, file, "INFO"));
            Console.SetError(new LogTeeWriter(Console.Error, file, "ERROR"));
            LogFilePath = path;
        }
        catch
        {
            // Logging must never stop the app from starting
        }
    }

    /// <summary>Keeps one previous log: caissalytics.log becomes caissalytics.1.log once it exceeds the limit.</summary>
    public static void RotateIfLarge(string path, long maxBytes)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.Length > maxBytes)
        {
            File.Move(path, Path.ChangeExtension(path, ".1.log"), overwrite: true);
        }
    }
}

/// <summary>Passes writes through to the original console and appends them line by line, timestamped, to a file.</summary>
public sealed class LogTeeWriter(TextWriter console, TextWriter file, string level) : TextWriter
{
    // Shared by the Out and Error writers, which append to the same file
    private static readonly object FileLock = new();
    // Longer lines are payload dumps, not diagnostics; keep the start so the source is still visible
    public const int MaxLineLength = 4000;
    private readonly StringBuilder _line = new();

    public override Encoding Encoding => console.Encoding;

    public override void Write(char value)
    {
        console.Write(value);
        lock (FileLock)
        {
            if (value == '\n')
            {
                FlushLineLocked();
            }
            else if (value != '\r')
            {
                _line.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        foreach (char c in value) Write(c);
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write('\n');
    }

    public override void Flush()
    {
        console.Flush();
        lock (FileLock)
        {
            if (_line.Length > 0) FlushLineLocked();
            file.Flush();
        }
    }

    private void FlushLineLocked()
    {
        try
        {
            string text = _line.Length <= MaxLineLength
                ? _line.ToString()
                : $"{_line.ToString(0, MaxLineLength)}... ({(_line.Length - MaxLineLength).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} more characters)";
            file.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {text}");
        }
        catch
        {
            // Disk full or file gone: keep the app running
        }
        _line.Clear();
    }
}

/// <summary>Routes framework log messages (warnings and errors, e.g. Blazor rendering exceptions) to Console.Error, and so to the log file.</summary>
public sealed class ConsoleErrorLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ConsoleErrorLogger(categoryName);

    public void Dispose() { }

    private sealed class ConsoleErrorLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message = formatter(state, exception);
            Console.Error.WriteLine(exception == null
                ? $"[{category}] {logLevel}: {message}"
                : $"[{category}] {logLevel}: {message}{Environment.NewLine}{exception}");
        }
    }
}
