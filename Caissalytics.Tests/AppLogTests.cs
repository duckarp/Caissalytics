using Caissalytics.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Caissalytics.Tests;

public class AppLogTests
{
    [Fact]
    public void TeeWriter_PassesThroughAndWritesTimestampedLines()
    {
        var console = new StringWriter();
        var file = new StringWriter();
        var tee = new LogTeeWriter(console, file, "ERROR");

        tee.WriteLine("[EngineManager] first");
        tee.Write("second ");
        tee.Write("part\r\n");

        Assert.Equal("[EngineManager] first\nsecond part\r\n", console.ToString());
        var lines = file.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[ERROR\] \[EngineManager\] first$", lines[0]);
        Assert.EndsWith("[ERROR] second part", lines[1]);
    }

    [Fact]
    public void TeeWriter_TruncatesHugeLinesInTheFileOnly()
    {
        var console = new StringWriter();
        var file = new StringWriter();
        var tee = new LogTeeWriter(console, file, "INFO");
        string huge = new string('x', LogTeeWriter.MaxLineLength + 5000);

        tee.WriteLine(huge);

        Assert.Equal(huge + "\n", console.ToString());
        string logged = file.ToString().TrimEnd();
        Assert.EndsWith("... (5,000 more characters)", logged);
        Assert.True(logged.Length < LogTeeWriter.MaxLineLength + 100);
    }

    [Fact]
    public void RotateIfLarge_KeepsOnePreviousLog()
    {
        string dir = Path.Combine(Path.GetTempPath(), "caissalytics_log_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "caissalytics.log");
            File.WriteAllText(path, new string('x', 100));

            AppLog.RotateIfLarge(path, maxBytes: 1000);
            Assert.True(File.Exists(path));

            AppLog.RotateIfLarge(path, maxBytes: 50);
            Assert.False(File.Exists(path));
            Assert.Equal(100, new FileInfo(Path.Combine(dir, "caissalytics.1.log")).Length);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoggerProvider_WritesWarningsAndErrorsOnly()
    {
        var logger = new ConsoleErrorLoggerProvider().CreateLogger("Renderer");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }
}
