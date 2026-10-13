namespace Caissalytics.Data;

public class ShortcutBinding
{
    public string Key { get; set; } = "";
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    public ShortcutBinding() { }

    public ShortcutBinding(string key, bool ctrl = false, bool alt = false, bool shift = false)
    {
        Key = key;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
    }

    public bool Matches(string key, bool ctrl, bool alt, bool shift)
    {
        return string.Equals(Key, key, StringComparison.OrdinalIgnoreCase)
            && Ctrl == ctrl
            && Alt == alt
            && Shift == shift;
    }

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");

        string keyDisplay = Key switch
        {
            "ArrowLeft" => "← Left",
            "ArrowRight" => "→ Right",
            "ArrowUp" => "↑ Up",
            "ArrowDown" => "↓ Down",
            "Home" => "Home",
            "End" => "End",
            "Space" => "Space",
            _ => Key
        };
        parts.Add(keyDisplay);

        return string.Join(" + ", parts);
    }
}

public class KeyboardShortcutSettings
{
    public ShortcutBinding NextMove { get; set; } = new("ArrowRight");
    public ShortcutBinding PreviousMove { get; set; } = new("ArrowLeft");
    public ShortcutBinding FirstMove { get; set; } = new("Home");
    public ShortcutBinding LastMove { get; set; } = new("End");

    // Variation navigation requested by user & tester
    public ShortcutBinding NextVariation { get; set; } = new("ArrowRight", ctrl: true);
    public ShortcutBinding PreviousVariation { get; set; } = new("ArrowLeft", ctrl: true);
    public ShortcutBinding AlternateNextVariation { get; set; } = new("ArrowDown");
    public ShortcutBinding AlternatePreviousVariation { get; set; } = new("ArrowUp");
}
