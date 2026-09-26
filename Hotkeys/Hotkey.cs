using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Hotkeys;

/// <summary>
/// A key combination in the form RegisterHotKey expects: modifiers as MOD_* bits plus a virtual
/// key code. Stored as text ("Ctrl+Alt+T"); <see cref="Describe"/> gives the display form
/// ("Ctrl + Alt + T").
/// </summary>
internal sealed record Hotkey(HotkeyModifiers Modifiers, Keys Key)
{
    public override string ToString() => string.Join("+", Parts(Key.ToString()));

    public string Describe() => string.Join(" + ", Parts(DescribeKey(Key)));

    private List<string> Parts(string key)
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(key);
        return parts;
    }

    /// <summary>Reads the text from the settings. Empty or unreadable gives null.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        Keys key = Keys.None;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Control; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win": modifiers |= HotkeyModifiers.Win; break;
                default:
                    // Enum.TryParse also accepts numbers - "1" would be the left mouse button.
                    if (raw.All(char.IsDigit) || !Enum.TryParse(raw, ignoreCase: true, out key))
                    {
                        return null;
                    }
                    break;
            }
        }

        return IsUsableKey(key) ? new Hotkey(modifiers, key) : null;
    }

    /// <summary>Modifiers alone are not a combination.</summary>
    public static bool IsUsableKey(Keys key) =>
        key is not (Keys.None or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin);

    private static string DescribeKey(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (key - Keys.NumPad0),
        Keys.Oemplus => "+",
        Keys.OemMinus => "-",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.Space => "Space",
        Keys.Prior => "Page Up",
        Keys.Next => "Page Down",
        Keys.Escape => "Esc",
        Keys.Return => "Enter",
        _ => key.ToString(),
    };
}

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}
