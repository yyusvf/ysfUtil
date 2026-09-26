using System.Runtime.InteropServices;
using YsfUtil.Hotkeys;
using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Features;

/// <summary>
/// Switches the taskbar between auto-hide and always shown.
///
/// Uses ABM_SETSTATE, the same interface the taskbar settings use: takes effect immediately
/// without restarting Explorer, and applies to the taskbars on all monitors.
/// </summary>
internal sealed class TaskbarToggleFeature : IFeature
{
    private const uint AbmGetState = 0x4;
    private const uint AbmSetState = 0xA;
    private const int AbsAutoHide = 0x1;
    private const int AbsAlwaysOnTop = 0x2;

    public string Id => "taskbar-toggle";

    public string Name => "Toggle taskbar";

    public string Description => "Switches between auto-hide and always shown.";

    // "Taskbar" in Segoe Fluent Icons
    public string Glyph => "";

    public Hotkey? DefaultHotkey => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.T);

    public string? Status => IsAutoHide ? "Auto-hidden" : "Always shown";

    public void Execute() => IsAutoHide = !IsAutoHide;

    public static bool IsAutoHide
    {
        get
        {
            AppBarData data = Data();
            return ((ulong)SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) != 0;
        }
        set
        {
            AppBarData data = Data();
            data.lParam = value ? AbsAutoHide : AbsAlwaysOnTop;
            SHAppBarMessage(AbmSetState, ref data);
        }
    }

    private static AppBarData Data() => new()
    {
        cbSize = Marshal.SizeOf<AppBarData>(),
        hWnd = FindWindow("Shell_TrayWnd", null),
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public int left, top, right, bottom;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);
}
