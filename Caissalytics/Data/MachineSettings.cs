using System.Text.Json;

namespace Caissalytics.Data;

/// <summary>
/// Appearance settings that describe this computer rather than the user (window state, graphics
/// acceleration). They stay in the local folder even when appearance_settings.json is in a shared data
/// folder. Null means "not set here yet", so the value from appearance_settings.json still applies.
/// </summary>
public class MachineSettings
{
    public const string FileName = "machine_settings.json";

    public bool? WindowMaximized { get; set; }
    public bool? HardwareAccelerationEnabled { get; set; }

    public static MachineSettings Load(string localDirectory)
    {
        try
        {
            string path = Path.Combine(localDirectory, FileName);
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<MachineSettings>(File.ReadAllText(path)) ?? new MachineSettings();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MachineSettings] Could not read {FileName}: {ex.Message}");
        }
        return new MachineSettings();
    }

    public static void Update(string localDirectory, Action<MachineSettings> change)
    {
        var settings = Load(localDirectory);
        change(settings);
        Directory.CreateDirectory(localDirectory);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(Path.Combine(localDirectory, FileName), json);
    }

    public void ApplyTo(AppearanceSettings appearance)
    {
        if (WindowMaximized is bool maximized) appearance.WindowMaximized = maximized;
        if (HardwareAccelerationEnabled is bool hwAccel) appearance.HardwareAccelerationEnabled = hwAccel;
    }

    /// <summary>Appearance settings as this computer sees them: the (possibly shared) file plus local overrides.</summary>
    public static AppearanceSettings LoadAppearance(string dataDirectory, string localDirectory)
    {
        AppearanceSettings appearance = new();
        try
        {
            string path = Path.Combine(dataDirectory, "appearance_settings.json");
            if (File.Exists(path))
            {
                appearance = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path)) ?? new AppearanceSettings();
            }
        }
        catch { }
        Load(localDirectory).ApplyTo(appearance);
        return appearance;
    }
}
