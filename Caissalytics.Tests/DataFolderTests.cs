using System.Text.Json;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class DataFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "caissalytics_datafolder_" + Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _shared;

    public DataFolderTests()
    {
        _local = Path.Combine(_root, "local");
        _shared = Path.Combine(_root, "shared");
        Directory.CreateDirectory(_local);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void SeedLocalData()
    {
        File.WriteAllText(Path.Combine(_local, "user_profile.json"), "{\"name\":\"local\"}");
        File.WriteAllText(Path.Combine(_local, "appearance_settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_local, "databases"));
        File.WriteAllText(Path.Combine(_local, "databases", "Mine.db"), "db");
        File.WriteAllText(Path.Combine(_local, "databases", "Mine.db-wal"), "wal");
        Directory.CreateDirectory(Path.Combine(_local, "engines"));
        File.WriteAllText(Path.Combine(_local, "engines", "engines_config.json"), "[]");
        File.WriteAllText(Path.Combine(_local, "skpp_session.json"), "{}");
    }

    [Fact]
    public void Resolve_WithoutConfig_UsesLocalFolder()
    {
        var result = AppPaths.Resolve(_local);

        Assert.Equal(_local, result.DataDirectory);
        Assert.Null(result.ConfiguredDirectory);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Switch_ToEmptyFolder_CopiesUserDataButNotLocalFiles()
    {
        SeedLocalData();

        AppPaths.SetConfiguredDirectory(_shared, _local, _local);
        var result = AppPaths.Resolve(_local);

        Assert.Equal(Path.GetFullPath(_shared), result.DataDirectory);
        Assert.Null(result.Error);
        Assert.Equal("{\"name\":\"local\"}", File.ReadAllText(Path.Combine(_shared, "user_profile.json")));
        Assert.True(File.Exists(Path.Combine(_shared, "databases", "Mine.db")));
        Assert.True(File.Exists(Path.Combine(_shared, "databases", "Mine.db-wal")));
        Assert.False(Directory.Exists(Path.Combine(_shared, "engines")));
        Assert.False(File.Exists(Path.Combine(_shared, "skpp_session.json")));
        // Originals stay where they were
        Assert.True(File.Exists(Path.Combine(_local, "user_profile.json")));
    }

    [Fact]
    public void Switch_ToFolderWithData_KeepsItsDataAndCopiesNothing()
    {
        SeedLocalData();
        Directory.CreateDirectory(Path.Combine(_shared, "databases"));
        File.WriteAllText(Path.Combine(_shared, "databases", "Club.db"), "club");

        AppPaths.SetConfiguredDirectory(_shared, _local, _local);
        var result = AppPaths.Resolve(_local);

        Assert.Equal(Path.GetFullPath(_shared), result.DataDirectory);
        Assert.False(File.Exists(Path.Combine(_shared, "user_profile.json")));
        Assert.False(File.Exists(Path.Combine(_shared, "databases", "Mine.db")));
    }

    [Fact]
    public void Resolve_CopiesOnlyOnce()
    {
        SeedLocalData();
        AppPaths.SetConfiguredDirectory(_shared, _local, _local);
        AppPaths.Resolve(_local);

        File.WriteAllText(Path.Combine(_local, "repertoire_data.json"), "{}");
        File.Delete(Path.Combine(_shared, "user_profile.json"));
        File.Delete(Path.Combine(_shared, "appearance_settings.json"));
        Directory.Delete(Path.Combine(_shared, "databases"), true);
        AppPaths.Resolve(_local);

        Assert.False(File.Exists(Path.Combine(_shared, "repertoire_data.json")));
        Assert.Equal(Path.GetFullPath(_shared), AppPaths.GetConfiguredDirectory(_local));
    }

    [Fact]
    public void Resolve_MissingConfiguredFolder_FallsBackToLocalWithError()
    {
        AppPaths.SetConfiguredDirectory(_shared, _local, _local);
        AppPaths.Resolve(_local);
        Directory.Delete(_shared, true); // share disconnected

        var result = AppPaths.Resolve(_local);

        Assert.Equal(_local, result.DataDirectory);
        Assert.NotNull(result.Error);
        Assert.False(Directory.Exists(_shared)); // not recreated as an empty folder
    }

    [Fact]
    public void ResetToDefault_ClearsConfiguredFolder()
    {
        AppPaths.SetConfiguredDirectory(_shared, _local, _local);
        AppPaths.SetConfiguredDirectory(null, _local, _shared);

        Assert.Null(AppPaths.GetConfiguredDirectory(_local));
        Assert.Equal(_local, AppPaths.Resolve(_local).DataDirectory);
    }

    [Fact]
    public void SetConfiguredDirectory_RejectsRelativePath()
    {
        Assert.Throws<ArgumentException>(() => AppPaths.SetConfiguredDirectory("relative/folder", _local, _local));
        Assert.Null(AppPaths.GetConfiguredDirectory(_local));
    }

    [Fact]
    public void CopyData_RemovesCopiedFilesWhenItFails()
    {
        SeedLocalData();
        Directory.CreateDirectory(Path.Combine(_shared, "databases"));
        // A folder where a database file should go makes File.Copy fail midway
        Directory.CreateDirectory(Path.Combine(_shared, "databases", "Mine.db-wal"));

        Assert.ThrowsAny<IOException>(() => AppPaths.CopyData(_local, _shared));

        Assert.False(File.Exists(Path.Combine(_shared, "user_profile.json")));
        Assert.False(File.Exists(Path.Combine(_shared, "databases", "Mine.db")));
    }

    [Fact]
    public void Lock_FromAnotherComputer_IsReportedUntilStale()
    {
        Directory.CreateDirectory(_shared);
        string lockPath = Path.Combine(_shared, DataFolderLock.FileName);
        var other = new DataFolderLockInfo { Machine = "OTHER-PC", User = "jana", ProcessId = 1234, HeartbeatUtc = DateTime.UtcNow };
        File.WriteAllText(lockPath, JsonSerializer.Serialize(other));

        Assert.Equal("OTHER-PC", DataFolderLock.FindOtherHolder(lockPath, DateTime.UtcNow)?.Machine);
        Assert.Null(DataFolderLock.FindOtherHolder(lockPath, DateTime.UtcNow + DataFolderLock.StaleAfter + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Lock_LeftByCrashedProcessOnThisComputer_IsIgnored()
    {
        Directory.CreateDirectory(_shared);
        string lockPath = Path.Combine(_shared, DataFolderLock.FileName);
        var crashed = new DataFolderLockInfo { Machine = Environment.MachineName, ProcessId = int.MaxValue, HeartbeatUtc = DateTime.UtcNow };
        File.WriteAllText(lockPath, JsonSerializer.Serialize(crashed));

        Assert.Null(DataFolderLock.FindOtherHolder(lockPath, DateTime.UtcNow));
    }

    [Fact]
    public void Lock_IsRemovedOnDispose()
    {
        Directory.CreateDirectory(_shared);
        using (var folderLock = DataFolderLock.Acquire(_shared, startHeartbeat: false))
        {
            Assert.Null(folderLock.OtherHolder);
            Assert.True(File.Exists(Path.Combine(_shared, DataFolderLock.FileName)));
        }
        Assert.False(File.Exists(Path.Combine(_shared, DataFolderLock.FileName)));
    }

    [Fact]
    public void MachineSettings_OverrideSharedAppearanceOnlyWhereSet()
    {
        Directory.CreateDirectory(_shared);
        var shared = new AppearanceSettings { BoardTheme = "green", WindowMaximized = true, HardwareAccelerationEnabled = true };
        File.WriteAllText(Path.Combine(_shared, "appearance_settings.json"), JsonSerializer.Serialize(shared));

        // Nothing set on this computer yet: the shared values apply (upgrade from a single folder)
        Assert.True(MachineSettings.LoadAppearance(_shared, _local).WindowMaximized);

        MachineSettings.Update(_local, m => m.HardwareAccelerationEnabled = false);
        var appearance = MachineSettings.LoadAppearance(_shared, _local);

        Assert.Equal("green", appearance.BoardTheme);
        Assert.False(appearance.HardwareAccelerationEnabled);
        Assert.True(appearance.WindowMaximized);
    }
}
