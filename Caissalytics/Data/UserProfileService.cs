using System.Runtime.InteropServices;
using System.Text.Json;

namespace Caissalytics.Data;

public class UserProfileService : IUserProfileService
{
    private readonly IOnlineGameSyncService? _syncService;
    private readonly string _profileFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event Action? OnProfileChanged;

    public UserProfileService(IOnlineGameSyncService? syncService = null)
    {
        _syncService = syncService;
        string configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics");
        Directory.CreateDirectory(configDir);
        _profileFilePath = Path.Combine(configDir, "user_profile.json");

        // Windows legacy migration if user had .local/share/Caissalytics
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !File.Exists(_profileFilePath))
        {
            string legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "Caissalytics", "user_profile.json");
            if (File.Exists(legacyPath))
            {
                try { File.Copy(legacyPath, _profileFilePath, overwrite: false); } catch { }
            }
        }
    }

    public UserProfileService(string customFilePath, IOnlineGameSyncService? syncService = null)
    {
        _syncService = syncService;
        _profileFilePath = customFilePath;
        var dir = Path.GetDirectoryName(_profileFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task<UserProfile> GetProfileAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_profileFilePath))
            {
                string json = await File.ReadAllTextAsync(_profileFilePath);
                var profile = JsonSerializer.Deserialize<UserProfile>(json);
                if (profile != null)
                {
                    return profile;
                }
            }

            // Fallback: If profile doesn't exist yet, check if online sync config has handles
            var defaultProfile = new UserProfile();
            if (_syncService != null)
            {
                try
                {
                    var syncConfig = await _syncService.GetConfigAsync();
                    defaultProfile.LichessUsername = syncConfig.LichessUsername;
                    defaultProfile.ChessComUsername = syncConfig.ChessComUsername;
                }
                catch { }
            }
            return defaultProfile;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserProfileService] Error reading profile: {ex.Message}");
            return new UserProfile();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveProfileAsync(UserProfile profile)
    {
        await _lock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(profile, options);
            await File.WriteAllTextAsync(_profileFilePath, json);

            // Also keep OnlineSyncConfig handles in sync
            if (_syncService != null)
            {
                try
                {
                    var syncConfig = await _syncService.GetConfigAsync();
                    syncConfig.LichessUsername = profile.LichessUsername?.Trim() ?? "";
                    syncConfig.ChessComUsername = profile.ChessComUsername?.Trim() ?? "";
                    await _syncService.SaveConfigAsync(syncConfig);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UserProfileService] Error syncing handles to OnlineSyncConfig: {ex.Message}");
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        OnProfileChanged?.Invoke();
    }
}
