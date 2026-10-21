using System.Text;

namespace Caissalytics.Data;

/// <summary>
/// Crash-safe writes for settings and state files: content goes to a temporary file in the
/// same folder, is flushed to disk, then renamed over the target. A crash or power loss
/// leaves either the old file or the new one, never a truncated mix.
/// </summary>
public static class AtomicFile
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <param name="ownerOnly">Create the file readable by the current user only (Unix 0600); use for files holding tokens.</param>
    public static void WriteAllText(string path, string contents, bool ownerOnly = false)
    {
        string tempPath = CreateTempPath(path);
        try
        {
            using (var stream = OpenTemp(tempPath, ownerOnly))
            {
                using (var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: -1, leaveOpen: true))
                {
                    writer.Write(contents);
                }
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <inheritdoc cref="WriteAllText"/>
    public static async Task WriteAllTextAsync(string path, string contents, bool ownerOnly = false, CancellationToken ct = default)
    {
        string tempPath = CreateTempPath(path);
        try
        {
            await using (var stream = OpenTemp(tempPath, ownerOnly))
            {
                await using (var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: -1, leaveOpen: true))
                {
                    await writer.WriteAsync(contents.AsMemory(), ct);
                }
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Moves an unreadable file aside (to "&lt;name&gt;.corrupt-&lt;timestamp&gt;") so that saving defaults
    /// afterwards does not destroy the user's data. Returns the new path, or null if nothing was moved.
    /// </summary>
    public static string? Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string target = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, target, overwrite: true);
            Console.Error.WriteLine($"[Caissalytics] Could not read '{path}'; moved it to '{target}'.");
            return target;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Caissalytics] Could not quarantine '{path}': {ex.Message}");
            return null;
        }
    }

    private static string CreateTempPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static FileStream OpenTemp(string tempPath, bool ownerOnly)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (ownerOnly && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        return new FileStream(tempPath, options);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
