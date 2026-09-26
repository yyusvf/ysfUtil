using System.Windows;
using System.Windows.Input;
using YsfUtil.Hotkeys;
using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Ui.Views;

/// <summary>
/// Records a key combination: click, press, done. Esc, Backspace or Delete remove it.
///
/// While recording, <see cref="RecordingChanged"/> tells the app to pause global hotkeys -
/// otherwise Windows would swallow an already registered combination before it arrives here.
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

    /// <summary>True when recording starts, false when it ends.</summary>
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
            // Just a modifier - keep waiting for the actual key.
            return;
        }

        HotkeyModifiers mods = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;

        // Without modifiers only function keys - otherwise e.g. "T" would be taken system-wide.
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
        Content = IsRecording ? "Press a combination…" : Hotkey?.Describe() ?? "No hotkey";
}
