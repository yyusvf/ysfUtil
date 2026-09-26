using System.Windows;
using System.Windows.Input;
using YsfUtil.Hotkeys;
using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Ui.Views;

/// <summary>
/// Nimmt eine Tastenkombination auf: anklicken, drücken, fertig. Esc, Rücktaste oder Entf
/// entfernen sie.
///
/// Während der Aufnahme meldet <see cref="RecordingChanged"/> das nach außen, damit die
/// systemweiten Hotkeys solange ruhen - sonst fängt Windows eine bereits belegte Kombination
/// ab, bevor sie hier ankommt.
/// </summary>
internal sealed class HotkeyBox : System.Windows.Controls.Button
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(Hotkey), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyChanged));

    public static readonly DependencyProperty IsRecordingProperty = DependencyProperty.Register(
        nameof(IsRecording), typeof(bool), typeof(HotkeyBox), new PropertyMetadata(false));

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(
        nameof(HasError), typeof(bool), typeof(HotkeyBox), new PropertyMetadata(false));

    public HotkeyBox()
    {
        Focusable = true;
        Click += (_, _) => IsRecording = true;
        LostFocus += (_, _) => IsRecording = false;
        UpdateText();
    }

    /// <summary>True beim Beginn einer Aufnahme, false an ihrem Ende.</summary>
    public static event Action<bool>? RecordingChanged;

    public Hotkey? Hotkey
    {
        get => (Hotkey?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public bool IsRecording
    {
        get => (bool)GetValue(IsRecordingProperty);
        set
        {
            if (IsRecording == value)
            {
                return;
            }
            SetValue(IsRecordingProperty, value);
            UpdateText();
            RecordingChanged?.Invoke(value);
        }
    }

    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsRecording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.None && key is Key.Escape or Key.Back or Key.Delete)
        {
            Hotkey = null;
            IsRecording = false;
            return;
        }

        var virtualKey = (Keys)KeyInterop.VirtualKeyFromKey(key);
        if (!Hotkeys.Hotkey.IsUsableKey(virtualKey))
        {
            // Nur ein Umschalter - weiter warten, bis die eigentliche Taste folgt.
            return;
        }

        HotkeyModifiers mods = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;

        // Ohne Umschalter nur Funktionstasten - sonst wäre z. B. "T" systemweit belegt.
        if (mods == HotkeyModifiers.None && virtualKey is not (>= Keys.F1 and <= Keys.F24))
        {
            return;
        }

        Hotkey = new Hotkey(mods, virtualKey);
        IsRecording = false;
    }

    private static void OnHotkeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((HotkeyBox)sender).UpdateText();

    private void UpdateText() =>
        Content = IsRecording ? "Kombination drücken …" : Hotkey?.Describe() ?? "Kein Hotkey";
}
