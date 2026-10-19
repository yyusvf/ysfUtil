using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using YsfUtil.Audio;
using Screen = System.Windows.Forms.Screen;

namespace YsfUtil.Ui.Views;

public partial class AudioSwitchBanner : Window
{
    private readonly DispatcherTimer timeout = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private int revision;
    public AudioSwitchBanner()
    {
        InitializeComponent();
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            int current = ++revision;
            Fade(0, 180, () => { if (current == revision) Hide(); });
        };
        Closed += (_, _) => timeout.Stop();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // Layered + WS_EX_TRANSPARENT passes mouse input to windows in other processes;
            // WPF IsHitTestVisible alone only disables input inside the WPF visual tree.
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x080000A0L));
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == 0x0084) { handled = true; return new IntPtr(-1); } // WM_NCHITTEST: HTTRANSPARENT
                return IntPtr.Zero;
            });
        };
    }

    internal void ShowDevice(AudioOutputDevice device)
        => ShowMessage("Audio output", device.Name, device.Glyph);

    internal void ShowError(string message)
        => ShowMessage("Audio output could not change", message, "\uE7BA");

    internal void ShowStartup(bool firstRun)
        => ShowMessage("ysfUtil is ready", firstRun
            ? "Find your controls in the tray menu (^)."
            : "Click the tray icon to open your controls.", "\uE73E");

    private void ShowMessage(string title, string message, string glyph)
    {
        ++revision;
        timeout.Stop();
        BannerTitle.Text = title;
        DeviceGlyph.Text = glyph;
        DeviceName.Text = message;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        UpdateLayout();
        var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        int marginX = (int)Math.Round(16 * dpi.DpiScaleX);
        int marginY = (int)Math.Round(16 * dpi.DpiScaleY);
        int left = area.Right - (int)Math.Round(ActualWidth * dpi.DpiScaleX) - marginX;
        int top = area.Bottom - (int)Math.Round(ActualHeight * dpi.DpiScaleY) - marginY;
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            left, top, 0, 0, 0x0015);
        Fade(1, 160);
        timeout.Start();
    }

    private void Fade(double opacity, int milliseconds, Action? completed = null)
    {
        double from = Opacity;
        BeginAnimation(OpacityProperty, null);
        Opacity = opacity;
        if (!SystemParameters.ClientAreaAnimation) { completed?.Invoke(); return; }
        var animation = new DoubleAnimation(from, opacity, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) => completed?.Invoke();
        BeginAnimation(OpacityProperty, animation);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
