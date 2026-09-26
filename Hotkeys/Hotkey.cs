using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Hotkeys;

/// <summary>
/// Eine Tastenkombination in der Form, die RegisterHotKey erwartet: Umschalter als MOD_*-Bits,
/// dazu eine Taste als virtueller Tastencode.
///
/// Als Text ("Ctrl+Alt+T") steht sie in den Einstellungen; <see cref="Describe"/> liefert die
/// deutsche Anzeige ("Strg + Alt + T").
/// </summary>
internal sealed record Hotkey(HotkeyModifiers Modifiers, Keys Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public string Describe()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Strg");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Umschalt");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(DescribeKey(Key));
        return string.Join(" + ", parts);
    }

    /// <summary>Liest den Text aus den Einstellungen. Leer oder unlesbar ergibt null.</summary>
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
                case "ctrl" or "strg" or "control": modifiers |= HotkeyModifiers.Control; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift" or "umschalt": modifiers |= HotkeyModifiers.Shift; break;
                case "win": modifiers |= HotkeyModifiers.Win; break;
                default:
                    // Enum.TryParse nimmt auch Zahlen an - "1" wäre sonst die linke Maustaste.
                    if (raw.All(char.IsDigit) || !Enum.TryParse(raw, ignoreCase: true, out key))
                    {
                        return null;
                    }
                    break;
            }
        }

        return IsUsableKey(key) ? new Hotkey(modifiers, key) : null;
    }

    /// <summary>Nur Umschalter ohne echte Taste ergeben keine Kombination.</summary>
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
        Keys.OemQuestion => "#",
        Keys.Space => "Leertaste",
        Keys.Prior => "Bild auf",
        Keys.Next => "Bild ab",
        Keys.Escape => "Esc",
        Keys.Return => "Eingabe",
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
