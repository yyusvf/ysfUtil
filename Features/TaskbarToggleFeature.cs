using System.Runtime.InteropServices;
using YsfUtil.Hotkeys;
using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Features;

/// <summary>
/// Schaltet die Taskleiste zwischen "automatisch ausblenden" und "immer anzeigen" um.
///
/// Über ABM_SETSTATE, dieselbe Schnittstelle, die auch die Taskleisten-Einstellungen nutzen:
/// die Änderung greift sofort, ohne den Explorer neu zu starten, und gilt für die Taskleisten
/// auf allen Bildschirmen.
/// </summary>
internal sealed class TaskbarToggleFeature : IFeature
{
    private const uint AbmGetState = 0x4;
    private const uint AbmSetState = 0xA;
    private const int AbsAutoHide = 0x1;
    private const int AbsAlwaysOnTop = 0x2;

    public string Id => "taskbar-toggle";

    public string Name => "Taskleiste umschalten";

    public string Description => "Wechselt zwischen automatisch ausblenden und immer anzeigen.";

    // "Taskleiste" aus Segoe Fluent Icons
    public string Glyph => "";

    public Hotkey? DefaultHotkey => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.T);

    public string? Status => IsAutoHide ? "Wird automatisch ausgeblendet" : "Wird immer angezeigt";

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
