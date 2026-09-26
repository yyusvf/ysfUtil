using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using YsfUtil.Ui.Shell;
using YsfUtil.Ui.Theme;
using YsfUtil.Ui.ViewModels;
using Screen = System.Windows.Forms.Screen;
using WinFormsCursor = System.Windows.Forms.Cursor;

namespace YsfUtil.Ui.Flyout;

/// <summary>
/// Das Panel am Infobereich. Es ist die einzige Oberfläche von ysfUtil: kein Eintrag in der
/// Taskleiste, keiner in Alt+Tab, und es verschwindet, sobald woanders hingeklickt wird.
/// </summary>
public partial class FlyoutWindow : Window
{
    /// <summary>
    /// Zeitfenster nach dem Ausblenden, in dem ein Klick aufs Tray-Symbol nicht wieder öffnet.
    /// Der Klick aufs Symbol nimmt dem Flyout zuerst den Fokus (es schließt sich), und erst danach
    /// kommt er beim Symbol an - ohne diese Sperre ginge es sofort wieder auf.
    /// </summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private const double EdgeMargin = 12;

    private DateTime hiddenAt = DateTime.MinValue;

    /// <summary>
    /// Worüber das Flyout beim letzten Öffnen gesetzt wurde (siehe <see cref="TrayAnchor"/>).
    /// Festgehalten statt bei jeder Größenänderung neu bestimmt: klappt der Überlauf später zu,
    /// soll das Flyout nicht hinterherspringen.
    /// </summary>
    private System.Drawing.Rectangle? anchor;

    /// <summary>
    /// Solange das Flyout offen, aber nicht aktiv ist, meldet Windows kein Deactivated - ein
    /// Klick woanders hin würde es also nicht schließen. Dafür schaut dieser Takt nach, wer
    /// gerade vorn ist.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer outsideWatch =
        new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>
    /// Was beim Öffnen vorn war: der Überlauf oder die Taskleiste. Wechselt der Fokus von dort
    /// weg - woandershin geklickt, Überlauf zugeklappt -, geht das Flyout mit zu.
    /// </summary>
    private IntPtr openedFrom;

    internal FlyoutWindow(IReadOnlyList<FeatureViewModel> features, SettingsViewModel settings)
    {
        InitializeComponent();
        FeatureList.ItemsSource = features;
        SettingsView.DataContext = settings;

        outsideWatch.Tick += (_, _) => CloseIfClickedOutside();
        Activated += (_, _) => outsideWatch.Stop();

        SourceInitialized += (_, _) =>
        {
            ApplyWindowEffects();
            HideFromAltTab();
        };
        ThemeManager.Changed += ApplyWindowEffects;

        Deactivated += (_, _) => HideFlyout();
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                PlaceAtTray();
            }
        };

        // Win+Pfeil hoch würde auch ein Flyout maximieren.
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }
        };
    }

    /// <summary>Kurz vor dem Einblenden - der Zustand der Funktionen wird dann frisch gelesen.</summary>
    internal event Action? Opening;

    internal event Action? ExitRequested;

    /// <summary>Für Linksklick aufs Tray-Symbol: auf, wenn zu - und umgekehrt.</summary>
    public void Toggle(System.Drawing.Rectangle? trayAnchor)
    {
        if (IsVisible)
        {
            HideFlyout();
        }
        else if (DateTime.UtcNow - hiddenAt > ReopenGuard)
        {
            ShowFlyout(trayAnchor);
        }
    }

    /// <summary>
    /// Zeigt das Flyout über <paramref name="trayAnchor"/>.
    ///
    /// Vom Tray-Symbol aus ohne Fokus, wie Razer Synapse: nähme das Flyout sofort den Fokus,
    /// klappte Windows den Überlauf zu, und das Flyout stünde über einer leeren Stelle. Aktiv
    /// wird es mit dem ersten Klick hinein.
    /// </summary>
    public void ShowFlyout(System.Drawing.Rectangle? trayAnchor, bool settings = false, bool activate = false)
    {
        Opening?.Invoke();
        SwitchView(settings);

        if (IsVisible)
        {
            Activate();
            return;
        }

        anchor = trayAnchor;

        IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
        WindowEffects.Cloak(handle, true);
        ShowActivated = activate;
        Show();
        UpdateLayout();
        PlaceAtTray();
        WindowEffects.Cloak(handle, false);

        if (activate)
        {
            Activate();
        }
        else
        {
            openedFrom = GetForegroundWindow();
            outsideWatch.Start();
        }
    }

    public void HideFlyout()
    {
        outsideWatch.Stop();
        if (!IsVisible)
        {
            return;
        }
        hiddenAt = DateTime.UtcNow;
        Hide();
    }

    /// <summary>
    /// Offen, aber nicht aktiv: zu, sobald weder das Fenster vorn ist, aus dem heraus geöffnet
    /// wurde, noch das Flyout selbst.
    /// </summary>
    private void CloseIfClickedOutside()
    {
        if (IsActive)
        {
            outsideWatch.Stop();
            return;
        }

        IntPtr front = GetForegroundWindow();
        if (front != new WindowInteropHelper(this).Handle && front != openedFrom)
        {
            HideFlyout();
        }
    }

    /// <summary>Für die Vorschau: gleich die Einstellungen zeigen.</summary>
    public void ShowSettingsView() => SwitchView(true);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // Nimmt ein Hotkey-Feld gerade auf, hat es die Taste schon als erledigt markiert.
        if (!e.Handled && e.Key == Key.Escape)
        {
            if (SettingsView.Visibility == Visibility.Visible)
            {
                SwitchView(false);
            }
            else
            {
                HideFlyout();
            }
            e.Handled = true;
        }
    }

    private void SwitchView(bool settings)
    {
        FeaturesView.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        Logo.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsButton.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        HeaderText.Text = settings ? "Einstellungen" : "ysfUtil";
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SwitchView(true);

    private void OnBack(object sender, RoutedEventArgs e) => SwitchView(false);

    private void OnExit(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    private void ApplyWindowEffects() => WindowEffects.Apply(this, ThemeManager.IsDark);

    /// <summary>
    /// Setzt das Flyout über das Tray-Symbol, mittig ausgerichtet und auf den Bildschirm
    /// begrenzt - bei seitlicher oder oberer Taskleiste entsprechend daneben oder darunter.
    /// Ohne bekannte Lage des Symbols (etwa beim Öffnen über einen zweiten Start) geht es in
    /// die Ecke an der Taskleiste.
    ///
    /// Gerechnet wird in Bildschirmpixeln, weil Bildschirme mit unterschiedlicher Skalierung
    /// sonst verschiedene Maßstäbe hätten - und mit dem sichtbaren Umriss, nicht mit dem
    /// Fensterrechteck: das schließt den unsichtbaren Rand ein, den der Rahmen mitbringt.
    /// </summary>
    private void PlaceAtTray()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out Rect32 outer))
        {
            return;
        }
        Rect32 visible = DwmGetWindowAttribute(handle, DwmExtendedFrameBounds, out Rect32 frame, Marshal.SizeOf<Rect32>()) == 0
            ? frame
            : outer;

        int width = visible.Right - visible.Left;
        int height = visible.Bottom - visible.Top;
        int margin = (int)Math.Round(EdgeMargin * VisualTreeHelper.GetDpi(this).DpiScaleX);
        TaskbarEdge edge = GetTaskbarEdge();

        int x, y;
        System.Drawing.Rectangle screen;
        if (anchor is System.Drawing.Rectangle a)
        {
            screen = Screen.FromRectangle(a).Bounds;
            int centreX = a.Left + a.Width / 2;
            int centreY = a.Top + a.Height / 2;
            (x, y) = edge switch
            {
                TaskbarEdge.Top => (centreX - width / 2, a.Bottom + margin),
                TaskbarEdge.Left => (a.Right + margin, centreY - height / 2),
                TaskbarEdge.Right => (a.Left - margin - width, centreY - height / 2),
                _ => (centreX - width / 2, a.Top - margin - height),
            };
        }
        else
        {
            screen = Screen.FromPoint(WinFormsCursor.Position).WorkingArea;
            x = edge == TaskbarEdge.Left ? screen.Left + margin : screen.Right - width - margin;
            y = edge == TaskbarEdge.Top ? screen.Top + margin : screen.Bottom - height - margin;
        }

        // Nie über den Bildschirmrand hinaus - ein Symbol ganz rechts würde das Flyout sonst
        // zur Hälfte hinausschieben.
        x = Math.Clamp(x, screen.Left + margin, Math.Max(screen.Left + margin, screen.Right - width - margin));
        y = Math.Clamp(y, screen.Top + margin, Math.Max(screen.Top + margin, screen.Bottom - height - margin));

        // Zurück vom sichtbaren Umriss aufs Fensterrechteck.
        x -= visible.Left - outer.Left;
        y -= visible.Top - outer.Top;

        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>Kein Eintrag in Alt+Tab - ein Flyout ist kein Fenster, zu dem man wechselt.</summary>
    private void HideFromAltTab()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow));
    }

    private static TaskbarEdge GetTaskbarEdge()
    {
        var data = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>() };
        return SHAppBarMessage(AbmGetTaskbarPos, ref data) != UIntPtr.Zero
            ? (TaskbarEdge)data.uEdge
            : TaskbarEdge.Bottom;
    }

    private enum TaskbarEdge : uint
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3,
    }

    private const uint AbmGetTaskbarPos = 0x5;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoZOrder = 0x4;
    private const uint SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Rect32 rc;
        public IntPtr lParam;
    }

    private const int DwmExtendedFrameBounds = 9;


    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();


    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect32 value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect32 rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}
