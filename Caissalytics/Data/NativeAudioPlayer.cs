using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Caissalytics.Data;

public static class NativeAudioPlayer
{
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private static string? _linuxPlayerExecutable;
    private static bool _linuxPlayerChecked = false;
    private static readonly object _initLock = new();

    [DllImport("winmm.dll", EntryPoint = "PlaySound", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool PlaySoundWin32(string pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_FILENAME = 0x00020000;

    public static string? GetSoundFilePath(ChessSoundType soundType)
    {
        string fileName = soundType switch
        {
            ChessSoundType.Move => "move.wav",
            ChessSoundType.Capture => "capture.wav",
            ChessSoundType.Check => "check.wav",
            ChessSoundType.Victory => "victory.wav",
            ChessSoundType.LowTime => "lowtime.wav",
            _ => "move.wav"
        };

        // Check common base directories for wwwroot/sounds
        string[] candidateDirs = [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "sounds"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "sounds"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "sounds"),
            Path.Combine(Directory.GetCurrentDirectory(), "Caissalytics", "wwwroot", "sounds"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Caissalytics", "wwwroot", "sounds"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Caissalytics", "wwwroot", "sounds"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Caissalytics", "wwwroot", "sounds")
        ];

        foreach (var dir in candidateDirs)
        {
            string fullPath = Path.Combine(dir, fileName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // If running as a standalone single-file binary, extract from embedded resources into user data directory
        try
        {
            string userSoundsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Caissalytics", "sounds");
            string extractedPath = Path.Combine(userSoundsDir, fileName);
            if (File.Exists(extractedPath))
            {
                return extractedPath;
            }

            var asm = typeof(NativeAudioPlayer).Assembly;
            string? resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"sounds.{fileName}", StringComparison.OrdinalIgnoreCase));
            if (resName != null)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    Directory.CreateDirectory(userSoundsDir);
                    using var fileStream = File.Create(extractedPath);
                    stream.CopyTo(fileStream);
                    return extractedPath;
                }
            }
        }
        catch { }

        return null;
    }

    public static bool Play(ChessSoundType soundType, float volumeRatio = 0.8f)
    {
        string? filePath = GetSoundFilePath(soundType);
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        volumeRatio = Math.Clamp(volumeRatio, 0f, 1f);

        try
        {
            if (IsLinux)
            {
                return PlayLinux(filePath, volumeRatio);
            }
            if (IsWindows)
            {
                return PlayWindows(filePath);
            }
            if (IsMac)
            {
                return PlayMac(filePath, volumeRatio);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NativeAudioPlayer] Playback error: {ex.Message}");
        }

        return false;
    }

    private static bool PlayLinux(string filePath, float volumeRatio)
    {
        EnsureLinuxPlayerDetected();
        if (string.IsNullOrEmpty(_linuxPlayerExecutable))
        {
            return false;
        }

        string args;
        string exe = _linuxPlayerExecutable;

        if (exe.EndsWith("pw-play", StringComparison.OrdinalIgnoreCase))
        {
            args = $"--volume={volumeRatio.ToString("F2", CultureInfo.InvariantCulture)} \"{filePath}\"";
        }
        else if (exe.EndsWith("paplay", StringComparison.OrdinalIgnoreCase))
        {
            int volInt = (int)(volumeRatio * 65536);
            args = $"--volume={volInt} \"{filePath}\"";
        }
        else if (exe.EndsWith("canberra-gtk-play", StringComparison.OrdinalIgnoreCase))
        {
            args = $"-f \"{filePath}\"";
        }
        else // aplay or other
        {
            args = $"-q \"{filePath}\"";
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        var process = Process.Start(psi);
        if (process != null)
        {
            // Auto-dispose when finished
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.WaitForExitAsync();
                    process.Dispose();
                }
                catch { }
            });
            return true;
        }

        return false;
    }

    private static bool PlayWindows(string filePath)
    {
        return PlaySoundWin32(filePath, IntPtr.Zero, SND_ASYNC | SND_FILENAME);
    }

    private static bool PlayMac(string filePath, float volumeRatio)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "afplay",
            Arguments = $"-v {volumeRatio.ToString("F2", CultureInfo.InvariantCulture)} \"{filePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        var process = Process.Start(psi);
        if (process != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.WaitForExitAsync();
                    process.Dispose();
                }
                catch { }
            });
            return true;
        }

        return false;
    }

    private static void EnsureLinuxPlayerDetected()
    {
        if (_linuxPlayerChecked) return;

        lock (_initLock)
        {
            if (_linuxPlayerChecked) return;
            _linuxPlayerChecked = true;

            // Prioritize modern PipeWire, then PulseAudio, then ALSA, then libcanberra
            string[] players = ["pw-play", "paplay", "aplay", "canberra-gtk-play"];
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return;

            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var candidate in players)
            {
                foreach (var path in paths)
                {
                    var full = Path.Combine(path, candidate);
                    if (File.Exists(full))
                    {
                        _linuxPlayerExecutable = full;
                        return;
                    }
                }
            }
        }
    }
}
