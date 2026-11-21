using System.Runtime.InteropServices;
using YsfUtil.Hotkeys;
using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Features;

/// <summary>
/// Switches between "PC screen only" and "Second screen only", like the Win+P panel.
///
/// Uses the same topology calls as Win+P (SetDisplayConfig with SDC_TOPOLOGY_*), so Windows
/// restores each mode's saved resolution and arrangement.
/// </summary>
internal sealed class DisplayModeToggleFeature : IFeature
{
    private const uint QdcDatabaseCurrent = 0x4;
    private const uint SdcApply = 0x80;
    private const uint TopologyInternal = 0x1;
    private const uint TopologyClone = 0x2;
    private const uint TopologyExtend = 0x4;
    private const uint TopologyExternal = 0x8;

    // Native sizes of DISPLAYCONFIG_PATH_INFO and DISPLAYCONFIG_MODE_INFO. Only the topology is
    // read here, so the arrays stay opaque buffers.
    private const int PathInfoSize = 72;
    private const int ModeInfoSize = 64;

    public string Id => "display-mode-toggle";
    public string Name => "Toggle display";
    public string Description => "Switches between PC screen only and second screen only.";
    public string Glyph => ""; // TVMonitor in Segoe Fluent Icons
    public Hotkey? DefaultHotkey => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.P);

    public string? Status => CurrentTopology() switch
    {
        TopologyInternal => "PC screen only",
        TopologyExternal => "Second screen only",
        TopologyExtend => "Extended",
        TopologyClone => "Duplicated",
        _ => null,
    };

    public Task ExecuteAsync()
    {
        // From any other mode (also Extend or Duplicate) the first press goes to PC screen only.
        uint target = CurrentTopology() == TopologyInternal ? TopologyExternal : TopologyInternal;
        int result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SdcApply | target);
        if (result != 0)
        {
            throw new InvalidOperationException(target == TopologyExternal
                ? "Windows could not switch to the second screen. Is it connected and switched on?"
                : $"Windows could not switch to the PC screen (error {result}).");
        }
        return Task.CompletedTask;
    }

    private static uint? CurrentTopology()
    {
        if (GetDisplayConfigBufferSizes(QdcDatabaseCurrent, out uint paths, out uint modes) != 0) return null;
        IntPtr pathBuffer = Marshal.AllocHGlobal((int)Math.Max(1, paths) * PathInfoSize);
        IntPtr modeBuffer = Marshal.AllocHGlobal((int)Math.Max(1, modes) * ModeInfoSize);
        try
        {
            return QueryDisplayConfig(QdcDatabaseCurrent, ref paths, pathBuffer, ref modes, modeBuffer, out uint topology) == 0
                ? topology
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(pathBuffer);
            Marshal.FreeHGlobal(modeBuffer);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr paths,
        ref uint modeCount, IntPtr modes, out uint topologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint pathCount, IntPtr paths, uint modeCount, IntPtr modes, uint flags);
}
