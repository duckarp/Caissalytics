namespace Caissalytics.Tests;

/// <summary>
/// A throwaway UCI "engine" written as a POSIX shell script, for exercising the real
/// process plumbing in UciProcessClient. Not available on Windows (tests return early there).
/// </summary>
public sealed class FakeUciEngine : IDisposable
{
    private readonly string _dir;

    public string ExecutablePath { get; }

    public static bool IsSupported => !OperatingSystem.IsWindows();

    /// <param name="caseBranches">Extra shell <c>case</c> branches matched against each input line, placed
    /// before the defaults (uci → uciok, isready → readyok, quit → exit). Variables POS (last
    /// "position fen" FEN) is maintained for you.</param>
    public FakeUciEngine(string caseBranches)
    {
        _dir = Path.Combine(Path.GetTempPath(), "caissalytics_fake_engine_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ExecutablePath = Path.Combine(_dir, "fake_engine.sh");

        string script = $$"""
            #!/bin/sh
            POS=""
            while read -r line; do
              case "$line" in
                "position fen "*) POS="${line#position fen }" ;;
            {{caseBranches}}
                uci) echo "id name FakeEngine"; echo "uciok" ;;
                isready) echo "readyok" ;;
                quit) exit 0 ;;
              esac
            done
            """;
        File.WriteAllText(ExecutablePath, script.Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ExecutablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
