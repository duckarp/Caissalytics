using System.Text.Json;

namespace Caissalytics.Data;

/// <summary>
/// Where the app keeps its files. <see cref="LocalDirectory"/> is always this computer's app data folder
/// (engines, logs, updates, logins, window state). <see cref="DataDirectory"/> holds the user's data and
/// appearance settings and can be moved, e.g. to a folder shared between computers on a LAN; the choice
/// itself lives in data_location.json in the local folder.
/// </summary>
public static class AppPaths
{
    public const string ConfigFileName = "data_location.json";
    public const string DatabasesFolderName = "databases";

    /// <summary>Files that belong to the movable data folder, besides the databases folder.</summary>
    public static readonly string[] DataFileNames =
    [
        "appearance_settings.json",
        "user_profile.json",
        "online_sync_config.json",
        "chess_club_settings.json",
        "repertoire_data.json",
        "homework_sheets.json",
        "puzzles_progress.json",
        "extracted_puzzles.json"
    ];

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Caissalytics");

    public static string LocalDirectory { get; private set; } = DefaultDirectory;

    public static string DataDirectory { get; private set; } = DefaultDirectory;

    /// <summary>The user chose a data folder other than the local one.</summary>
    public static bool IsCustomDataDirectory => !SamePath(DataDirectory, LocalDirectory);

    /// <summary>Why the configured data folder is not in use (or its data was not copied), shown to the user.</summary>
    public static string? StartupError { get; private set; }

    public static void Initialize(DataLocationResult result)
    {
        LocalDirectory = result.LocalDirectory;
        DataDirectory = result.DataDirectory;
        StartupError = result.Error;
    }

    /// <summary>
    /// Works out which data folder to use at startup. A pending switch copies the current data into the
    /// new folder first, unless that folder already holds Caissalytics data (set up from another computer).
    /// An unreachable folder (unmounted share) falls back to the previous one so the app still starts.
    /// </summary>
    public static DataLocationResult Resolve(string localDirectory)
    {
        var config = ReadConfig(localDirectory);
        string? configured = string.IsNullOrWhiteSpace(config.DataDirectory) ? null : config.DataDirectory;
        string target = configured ?? localDirectory;
        string? pendingFrom = config.PendingCopyFrom;

        string? error = null;
        try
        {
            // Only a fresh switch may create the folder; a missing one otherwise means the share is offline
            if (pendingFrom != null || configured == null)
            {
                Directory.CreateDirectory(target);
            }
            else if (!Directory.Exists(target))
            {
                throw new DirectoryNotFoundException("the folder does not exist or the network share is not connected");
            }
            EnsureWritable(target);
        }
        catch (Exception ex)
        {
            error = $"Data folder {target} is not available ({ex.Message}).";
            Console.Error.WriteLine($"[AppPaths] {error}");
            string fallback = pendingFrom != null && Directory.Exists(pendingFrom) ? pendingFrom : localDirectory;
            return new DataLocationResult(localDirectory, fallback, configured, error);
        }

        if (pendingFrom != null)
        {
            if (!SamePath(pendingFrom, target) && Directory.Exists(pendingFrom) && !HasData(target))
            {
                try
                {
                    CopyData(pendingFrom, target);
                    Console.WriteLine($"[AppPaths] Copied data from {pendingFrom} to {target}");
                }
                catch (Exception ex)
                {
                    // Keep the switch pending so the copy is retried on the next start
                    error = $"Could not copy data to {target} ({ex.Message}); still using {pendingFrom}.";
                    Console.Error.WriteLine($"[AppPaths] {error}");
                    return new DataLocationResult(localDirectory, pendingFrom, configured, error);
                }
            }

            try
            {
                WriteConfig(localDirectory, config with { PendingCopyFrom = null });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AppPaths] Could not clear the pending data copy: {ex.Message}");
            }
        }

        return new DataLocationResult(localDirectory, target, configured, null);
    }

    /// <summary>The data folder chosen for the next start, or null for the local default.</summary>
    public static string? GetConfiguredDirectory(string? localDirectory = null)
    {
        var config = ReadConfig(localDirectory ?? LocalDirectory);
        return string.IsNullOrWhiteSpace(config.DataDirectory) ? null : config.DataDirectory;
    }

    /// <summary>
    /// Chooses the data folder for the next start; null or the local folder goes back to the default.
    /// Throws when the folder can't be created or written to.
    /// </summary>
    public static void SetConfiguredDirectory(string? newDirectory, string? localDirectory = null, string? currentDataDirectory = null)
    {
        string local = localDirectory ?? LocalDirectory;
        string current = currentDataDirectory ?? DataDirectory;
        string? normalized = null;

        if (!string.IsNullOrWhiteSpace(newDirectory))
        {
            string trimmed = newDirectory.Trim();
            if (!Path.IsPathRooted(trimmed))
            {
                throw new ArgumentException("Enter a full folder path.");
            }
            normalized = Path.GetFullPath(trimmed);
            Directory.CreateDirectory(normalized);
            EnsureWritable(normalized);
            if (SamePath(normalized, local))
            {
                normalized = null;
            }
        }

        string target = normalized ?? local;
        WriteConfig(local, new DataLocationConfig
        {
            DataDirectory = normalized,
            PendingCopyFrom = SamePath(target, current) ? null : current
        });
    }

    /// <summary>The folder already holds Caissalytics data, so switching to it must not copy over it.</summary>
    public static bool HasData(string directory)
    {
        if (DataFileNames.Any(name => File.Exists(Path.Combine(directory, name))))
        {
            return true;
        }
        string databases = Path.Combine(directory, DatabasesFolderName);
        return Directory.Exists(databases) && Directory.EnumerateFiles(databases, "*.db").Any();
    }

    /// <summary>Copies the data files and databases; on failure removes what it copied so a retry starts clean.</summary>
    public static void CopyData(string from, string to)
    {
        var copied = new List<string>();
        try
        {
            Directory.CreateDirectory(to);
            foreach (string name in DataFileNames)
            {
                CopyIfPresent(Path.Combine(from, name), Path.Combine(to, name), copied);
            }

            string fromDatabases = Path.Combine(from, DatabasesFolderName);
            if (Directory.Exists(fromDatabases))
            {
                string toDatabases = Path.Combine(to, DatabasesFolderName);
                Directory.CreateDirectory(toDatabases);
                // Includes -wal/-shm files, which may still hold committed changes
                foreach (string file in Directory.GetFiles(fromDatabases))
                {
                    CopyIfPresent(file, Path.Combine(toDatabases, Path.GetFileName(file)), copied);
                }
            }
        }
        catch
        {
            foreach (string file in copied)
            {
                try { File.Delete(file); } catch { }
            }
            throw;
        }
    }

    public static bool SamePath(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            comparison);
    }

    private static void CopyIfPresent(string source, string destination, List<string> copied)
    {
        if (!File.Exists(source) || File.Exists(destination))
        {
            return;
        }
        File.Copy(source, destination);
        copied.Add(destination);
    }

    private static void EnsureWritable(string directory)
    {
        string probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
        File.WriteAllText(probe, "");
        File.Delete(probe);
    }

    private static DataLocationConfig ReadConfig(string localDirectory)
    {
        try
        {
            string path = Path.Combine(localDirectory, ConfigFileName);
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<DataLocationConfig>(File.ReadAllText(path)) ?? new DataLocationConfig();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AppPaths] Could not read {ConfigFileName}: {ex.Message}");
        }
        return new DataLocationConfig();
    }

    private static void WriteConfig(string localDirectory, DataLocationConfig config)
    {
        Directory.CreateDirectory(localDirectory);
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(Path.Combine(localDirectory, ConfigFileName), json);
    }
}

public record DataLocationConfig
{
    public string? DataDirectory { get; init; }

    /// <summary>The data folder in use when the switch was chosen; its data is copied over on the next start.</summary>
    public string? PendingCopyFrom { get; init; }
}

/// <param name="ConfiguredDirectory">The folder the user chose, null for the default.</param>
/// <param name="Error">Set when the chosen folder could not be used.</param>
public record DataLocationResult(string LocalDirectory, string DataDirectory, string? ConfiguredDirectory, string? Error);
