using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YsfUtil.Hotkeys;

/// <summary>
/// Invisible window all global hotkeys are registered with. Also receives a second instance's
/// request to open the flyout. Separate from the flyout so hotkeys work from startup on.
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

    /// <summary>Registers the combination. False if another app already owns it.</summary>
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
