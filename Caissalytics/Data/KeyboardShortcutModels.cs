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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key);

    public bool Matches(string key, bool ctrl, bool alt, bool shift)
    {
        if (string.IsNullOrWhiteSpace(Key)) return false;

        bool keyMatches;
        if (string.Equals(Key, "Space", StringComparison.OrdinalIgnoreCase) || Key == " ")
        {
            keyMatches = key == " " || string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            keyMatches = string.Equals(Key, key, StringComparison.OrdinalIgnoreCase);
        }

        return keyMatches
            && Ctrl == ctrl
            && Alt == alt
            && Shift == shift;
    }

    public string ToDisplayString()
    {
        if (string.IsNullOrWhiteSpace(Key)) return "—";

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
            "Space" or " " => "Space",
            _ => Key.Length == 1 ? Key.ToUpperInvariant() : Key
        };
        parts.Add(keyDisplay);

        return string.Join(" + ", parts);
    }
}

public class KeyEventDto
{
    public string Key { get; set; } = "";
    public bool CtrlKey { get; set; }
    public bool AltKey { get; set; }
    public bool ShiftKey { get; set; }
    public string Code { get; set; } = "";
}

public class KeyboardShortcutSettings
{
    public ShortcutBinding NextMove { get; set; } = new("ArrowRight");
    public ShortcutBinding NextMoveAlt { get; set; } = new();

    public ShortcutBinding PreviousMove { get; set; } = new("ArrowLeft");
    public ShortcutBinding PreviousMoveAlt { get; set; } = new();

    public ShortcutBinding FirstMove { get; set; } = new("Home");
    public ShortcutBinding FirstMoveAlt { get; set; } = new("ArrowUp");

    public ShortcutBinding LastMove { get; set; } = new("End");
    public ShortcutBinding LastMoveAlt { get; set; } = new("ArrowDown");

    public ShortcutBinding NextVariation { get; set; } = new("ArrowRight", ctrl: true);
    public ShortcutBinding NextVariationAlt { get; set; } = new();

    public ShortcutBinding PreviousVariation { get; set; } = new("ArrowLeft", ctrl: true);
    public ShortcutBinding PreviousVariationAlt { get; set; } = new();

    // Toggle Engine (l)
    public ShortcutBinding ToggleEngine { get; set; } = new("l");
    public ShortcutBinding ToggleEngineAlt { get; set; } = new();

    // Play Best Move (Space)
    public ShortcutBinding BestMove { get; set; } = new("Space");
    public ShortcutBinding BestMoveAlt { get; set; } = new();

    // Board Navigation (f)
    public ShortcutBinding FlipBoard { get; set; } = new("f");
    public ShortcutBinding FlipBoardAlt { get; set; } = new();

    // Tab Management (Ctrl+W)
    public ShortcutBinding CloseTab { get; set; } = new("w", ctrl: true);
    public ShortcutBinding CloseTabAlt { get; set; } = new();

    // Backward compatibility for legacy JSON settings
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ShortcutBinding? AlternateNextVariation { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ShortcutBinding? AlternatePreviousVariation { get; set; }

    public bool MatchesNextMove(string key, bool ctrl, bool alt, bool shift) =>
        NextMove.Matches(key, ctrl, alt, shift) || NextMoveAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesPreviousMove(string key, bool ctrl, bool alt, bool shift) =>
        PreviousMove.Matches(key, ctrl, alt, shift) || PreviousMoveAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesFirstMove(string key, bool ctrl, bool alt, bool shift) =>
        FirstMove.Matches(key, ctrl, alt, shift) || FirstMoveAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesLastMove(string key, bool ctrl, bool alt, bool shift) =>
        LastMove.Matches(key, ctrl, alt, shift) || LastMoveAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesNextVariation(string key, bool ctrl, bool alt, bool shift) =>
        NextVariation.Matches(key, ctrl, alt, shift) || NextVariationAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesPreviousVariation(string key, bool ctrl, bool alt, bool shift) =>
        PreviousVariation.Matches(key, ctrl, alt, shift) || PreviousVariationAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesToggleEngine(string key, bool ctrl, bool alt, bool shift) =>
        ToggleEngine.Matches(key, ctrl, alt, shift) || ToggleEngineAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesBestMove(string key, bool ctrl, bool alt, bool shift) =>
        BestMove.Matches(key, ctrl, alt, shift) || BestMoveAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesFlipBoard(string key, bool ctrl, bool alt, bool shift) =>
        FlipBoard.Matches(key, ctrl, alt, shift) || FlipBoardAlt.Matches(key, ctrl, alt, shift);

    public bool MatchesCloseTab(string key, bool ctrl, bool alt, bool shift) =>
        CloseTab.Matches(key, ctrl, alt, shift) || CloseTabAlt.Matches(key, ctrl, alt, shift);
}
