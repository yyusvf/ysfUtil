using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using YsfUtil.Ui.Theme;
using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Ui;

/// <summary>
/// The notification area icon. Left click toggles the flyout, right click opens a menu with all
/// enabled features - themed like the flyout.
/// </summary>
internal sealed class TrayController : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip menu = new();
    private readonly Icon icon;
    private readonly IntPtr iconHandle;
    private readonly IReadOnlyList<FeatureViewModel> features;
    private Rectangle? pressAnchor;

    public TrayController(IReadOnlyList<FeatureViewModel> features)
    {
        this.features = features;

        using (Bitmap bitmap = AppIconArt.Render(32))
        {
            iconHandle = bitmap.GetHicon();
            icon = Icon.FromHandle(iconHandle);
        }

        // Own renderer, otherwise the menu would always be light. See ThemedMenuRenderer.
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new ThemedMenuRenderer();

        notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "ysfUtil",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // The menu is rebuilt on right mouse down, before NotifyIcon shows it. Rebuilding in
        // Opening makes Windows discard the menu on the first click.
        //
        // On left mouse down the icon position is captured: an open overflow is still open now,
        // but may already be closed on mouse up.
        notifyIcon.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                RebuildMenu();
            }
            else if (e.Button == MouseButtons.Left)
            {
                pressAnchor = Flyout.TrayAnchor.Resolve(GetIconBounds());
            }
        };
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                FlyoutToggleRequested?.Invoke(pressAnchor ?? Flyout.TrayAnchor.Resolve(GetIconBounds()));
                pressAnchor = null;
            }
        };
    }

    /// <summary>Left click: toggle the flyout, with the spot it should sit above.</summary>
    public event Action<Rectangle?>? FlyoutToggleRequested;

    /// <summary>From the menu: open the flyout, optionally on the settings view.</summary>
    public event Action<bool>? FlyoutRequested;

    public event Action<FeatureViewModel>? RunRequested;

    public event Action? AutostartToggled;

    public event Action? ExitRequested;

    /// <summary>
    /// Where the icon currently is, in screen pixels - on the taskbar or in the open overflow (^).
    /// Null if Windows doesn't say, e.g. while the icon sits in the closed overflow.
    ///
    /// NotifyIcon doesn't expose the window and id it registered with; both are private fields.
    /// Looked up by type rather than name, since names and the id type vary between versions.
    /// </summary>
    public Rectangle? GetIconBounds()
    {
        try
        {
            FieldInfo[] fields = typeof(NotifyIcon).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            NativeWindow? native = fields
                .Where(f => typeof(NativeWindow).IsAssignableFrom(f.FieldType))
                .Select(f => f.GetValue(notifyIcon) as NativeWindow)
                .FirstOrDefault(w => w != null);
            object? id = fields
                .FirstOrDefault(f => f.Name.TrimStart('_').Equals("id", StringComparison.OrdinalIgnoreCase))
                ?.GetValue(notifyIcon);
            if (native == null || id == null)
            {
                return null;
            }

            var identifier = new NotifyIconIdentifier
            {
                cbSize = Marshal.SizeOf<NotifyIconIdentifier>(),
                hWnd = native.Handle,
                uID = Convert.ToUInt32(id),
            };
            if (Shell_NotifyIconGetRect(ref identifier, out IconRect r) != 0 || r.Right <= r.Left)
            {
                return null;
            }
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
        catch
        {
            return null;
        }
    }

    public void ShowHint(string title, string message, ToolTipIcon kind = ToolTipIcon.Info)
    {
        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.BalloonTipIcon = kind;
        notifyIcon.ShowBalloonTip(4000);
    }

    private void RebuildMenu()
    {
        menu.Items.Clear();
        menu.BackColor = Palette.Window;
        menu.ForeColor = Palette.Text;

        foreach (FeatureViewModel feature in features)
        {
            if (!feature.IsEnabled)
            {
                continue;
            }

            feature.Refresh();
            var item = new ToolStripMenuItem(feature.Name)
            {
                ShortcutKeyDisplayString = feature.Hotkey?.Describe(),
                ToolTipText = feature.Status,
            };
            item.Click += (_, _) => RunRequested?.Invoke(feature);
            menu.Items.Add(Style(item));

            if (feature.Status is string status)
            {
                menu.Items.Add(Style(new ToolStripMenuItem("    " + status) { Enabled = false }));
            }
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }

        var open = new ToolStripMenuItem("Open ysfUtil") { Font = new Font(menu.Font, FontStyle.Bold) };
        open.Click += (_, _) => FlyoutRequested?.Invoke(false);
        menu.Items.Add(Style(open));

        var settings = new ToolStripMenuItem("Settings");
        settings.Click += (_, _) => FlyoutRequested?.Invoke(true);
        menu.Items.Add(Style(settings));

        var autostart = new ToolStripMenuItem("Start with Windows") { Checked = Autostart.IsEnabled() };
        autostart.Click += (_, _) => AutostartToggled?.Invoke();
        menu.Items.Add(Style(autostart));

        var exit = new ToolStripMenuItem("Quit");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(Style(exit));
    }

    /// <summary>Text colour per item - disabled items would otherwise use the system grey, barely readable on dark.</summary>
    private static ToolStripMenuItem Style(ToolStripMenuItem item)
    {
        item.BackColor = Palette.Window;
        item.ForeColor = item.Enabled ? Palette.Text : Palette.TextDisabled;
        return item;
    }

    public void Dispose()
    {
        // Without hiding explicitly, the icon lingers until the mouse passes over it.
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        icon.Dispose();
        DestroyIcon(iconHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out IconRect location);
}
