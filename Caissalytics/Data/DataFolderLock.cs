using System.Diagnostics;
using System.Text.Json;

namespace Caissalytics.Data;

/// <summary>
/// Marks the data folder as in use so a second computer opening the same shared folder can warn the user:
/// the SQLite databases and JSON settings are not safe to write from two places at once. The holder
/// refreshes the timestamp every minute; a lock that stopped being refreshed belongs to a crashed app.
/// </summary>
public sealed class DataFolderLock : IDisposable
{
    public const string FileName = "caissalytics.lock";
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(3);

    private readonly string _path;
    private readonly DataFolderLockInfo _info;
    private readonly Timer? _heartbeat;

    /// <summary>Who else had the folder open when this app started, or null.</summary>
    public DataFolderLockInfo? OtherHolder { get; }

    public static DataFolderLock? Current { get; private set; }

    private DataFolderLock(string directory, bool startHeartbeat)
    {
        _path = Path.Combine(directory, FileName);
        OtherHolder = FindOtherHolder(_path, DateTime.UtcNow);
        _info = new DataFolderLockInfo
        {
            Machine = Environment.MachineName,
            User = Environment.UserName,
            ProcessId = Environment.ProcessId,
            StartedUtc = DateTime.UtcNow,
            HeartbeatUtc = DateTime.UtcNow
        };
        Write();
        if (startHeartbeat)
        {
            _heartbeat = new Timer(_ => Write(), null, HeartbeatInterval, HeartbeatInterval);
        }
    }

    public static DataFolderLock Acquire(string directory, bool startHeartbeat = true)
    {
        var folderLock = new DataFolderLock(directory, startHeartbeat);
        Current = folderLock;
        return folderLock;
    }

    public static DataFolderLockInfo? FindOtherHolder(string lockPath, DateTime nowUtc)
    {
        try
        {
            if (!File.Exists(lockPath))
            {
                return null;
            }
            var info = JsonSerializer.Deserialize<DataFolderLockInfo>(File.ReadAllText(lockPath));
            if (info == null || nowUtc - info.HeartbeatUtc > StaleAfter)
            {
                return null;
            }
            bool sameMachine = string.Equals(info.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            if (sameMachine && (info.ProcessId == Environment.ProcessId || !IsProcessRunning(info.ProcessId)))
            {
                return null;
            }
            return info;
        }
        catch
        {
            return null;
        }
    }

    private void Write()
    {
        try
        {
            _info.HeartbeatUtc = DateTime.UtcNow;
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_info));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DataFolderLock] Could not write {_path}: {ex.Message}");
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _heartbeat?.Dispose();
        try
        {
            // Leave a lock that another computer took over in the meantime
            var info = JsonSerializer.Deserialize<DataFolderLockInfo>(File.ReadAllText(_path));
            if (info != null && info.Machine == _info.Machine && info.ProcessId == _info.ProcessId)
            {
                File.Delete(_path);
            }
        }
        catch { }
        if (Current == this)
        {
            Current = null;
        }
    }
}

public class DataFolderLockInfo
{
    public string Machine { get; set; } = "";
    public string User { get; set; } = "";
    public int ProcessId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime HeartbeatUtc { get; set; }
}
