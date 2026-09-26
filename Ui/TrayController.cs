using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using YsfUtil.Ui.Theme;
using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Ui;

/// <summary>
/// Symbol im Infobereich. Linksklick klappt das Flyout auf und zu, Rechtsklick öffnet das Menü
/// mit allen eingeschalteten Funktionen - im selben Farbsatz wie das Flyout.
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

    public TrayController(IReadOnlyList<FeatureViewModel> features)
    {
        this.features = features;

        using (Bitmap bitmap = AppIconArt.Render(32))
        {
            iconHandle = bitmap.GetHicon();
            icon = Icon.FromHandle(iconHandle);
        }

        // Eigener Renderer, sonst wäre das Menü immer hell. Siehe ThemedMenuRenderer.
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new ThemedMenuRenderer();

        notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "ysfUtil",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Aufbauen beim Drücken der rechten Taste, also bevor NotifyIcon das Menü zeigt. Im
        // Opening-Ereignis verwirft Windows ein frisch umgebautes Menü beim ersten Klick.
        //
        // Beim Drücken der linken Taste wird festgehalten, wo das Symbol steht: jetzt ist ein
        // aufgeklappter Überlauf noch offen, beim Loslassen womöglich schon nicht mehr.
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

    private Rectangle? pressAnchor;

    /// <summary>Linksklick: Flyout auf oder zu - mit der Stelle, über der es stehen soll.</summary>
    public event Action<Rectangle?>? FlyoutToggleRequested;

    /// <summary>Aus dem Menü: Flyout öffnen, auf Wunsch gleich mit den Einstellungen.</summary>
    public event Action<bool>? FlyoutRequested;

    public event Action<FeatureViewModel>? RunRequested;

    public event Action? AutostartToggled;

    public event Action? ExitRequested;

    /// <summary>
    /// Wo das Symbol gerade auf dem Bildschirm steht, in Bildschirmpixeln - auf der Taskleiste
    /// oder im aufgeklappten Überlauf (^). Null, wenn Windows es nicht verrät, etwa weil das
    /// Symbol im zugeklappten Überlauf steckt.
    ///
    /// NotifyIcon gibt Fenster und Kennung, unter denen es bei Windows angemeldet ist, nicht
    /// heraus; beides steht in privaten Feldern (in .NET 8 mit Unterstrich, früher ohne).
    /// </summary>
    public Rectangle? GetIconBounds()
    {
        try
        {
            // Nach Typ statt nach Namen gesucht: die Felder heißen je nach Fassung anders, und
            // die Kennung war mal int, mal uint.
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

        var open = new ToolStripMenuItem("ysfUtil öffnen") { Font = new Font(menu.Font, FontStyle.Bold) };
        open.Click += (_, _) => FlyoutRequested?.Invoke(false);
        menu.Items.Add(Style(open));

        var settings = new ToolStripMenuItem("Einstellungen");
        settings.Click += (_, _) => FlyoutRequested?.Invoke(true);
        menu.Items.Add(Style(settings));

        var autostart = new ToolStripMenuItem("Mit Windows starten") { Checked = Autostart.IsEnabled() };
        autostart.Click += (_, _) => AutostartToggled?.Invoke();
        menu.Items.Add(Style(autostart));

        var exit = new ToolStripMenuItem("Beenden");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(Style(exit));
    }

    /// <summary>Textfarbe je Eintrag - abgeschaltete nähmen sonst das Systemgrau, im Dunklen kaum lesbar.</summary>
    private static ToolStripMenuItem Style(ToolStripMenuItem item)
    {
        item.BackColor = Palette.Window;
        item.ForeColor = item.Enabled ? Palette.Text : Palette.TextDisabled;
        return item;
    }

    public void Dispose()
    {
        // Ohne ausdrückliches Ausblenden bleibt das Symbol stehen, bis jemand darüberfährt.
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        icon.Dispose();
        DestroyIcon(iconHandle);
    }
}
