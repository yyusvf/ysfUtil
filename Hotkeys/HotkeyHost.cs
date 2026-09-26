using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YsfUtil.Hotkeys;

/// <summary>
/// Unsichtbares Fenster, bei dem alle systemweiten Hotkeys angemeldet sind. Empfängt außerdem
/// die Nachricht einer zweiten Instanz, das Fenster zu zeigen.
///
/// Bewusst ein eigenes Fenster und nicht das Hauptfenster: das entsteht erst beim ersten Öffnen,
/// die Hotkeys sollen aber vom Start an wirken.
/// </summary>
internal sealed class HotkeyHost : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly Dictionary<int, Action> actions = [];
    private int nextId = 1;

    public HotkeyHost()
    {
        CreateHandle(new CreateParams());
    }

    public event Action? ShowWindowRequested;

    /// <summary>Meldet die Kombination an. False, wenn ein anderes Programm sie schon belegt.</summary>
    public bool Register(Hotkey hotkey, Action action)
    {
        int id = nextId++;
        if (!RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | ModNoRepeat, (uint)hotkey.Key))
        {
            return false;
        }
        actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in actions.Keys)
        {
            UnregisterHotKey(Handle, id);
        }
        actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && actions.TryGetValue(m.WParam.ToInt32(), out Action? action))
        {
            action();
        }
        else if (SingleInstance.ShowWindowMessage != 0 && m.Msg == (int)SingleInstance.ShowWindowMessage)
        {
            ShowWindowRequested?.Invoke();
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
