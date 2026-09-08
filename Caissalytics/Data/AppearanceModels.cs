namespace Caissalytics.Data;

public enum ChessSoundType
{
    Move,
    Capture,
    Check,
    Victory,
    LowTime
}

public class AppearanceSettings
{
    private int _volume = 80;

    public string BoardTheme { get; set; } = "brown"; // "brown", "green", "blue", "slate", "marble", "monochrome"
    public string PieceSet { get; set; } = "cburnett";
    public string Language { get; set; } = "en";
    public bool SoundEnabled { get; set; } = true;

    public int Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 100);
    }

    public bool PlayMoveSound { get; set; } = true;
    public bool PlayCaptureSound { get; set; } = true;
    public bool PlayCheckSound { get; set; } = true;
    public bool PlayVictorySound { get; set; } = true;
    public bool PlayLowTimeSound { get; set; } = true;

    public static readonly string[] AvailableBoardThemes =
    [
        "brown",
        "green",
        "blue",
        "slate",
        "marble",
        "monochrome"
    ];
}
