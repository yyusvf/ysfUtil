using YsfUtil.Ui.Shell;

namespace YsfUtil;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (Updates.PortableUpdater.IsHelper(args))
        {
            Environment.ExitCode = Updates.PortableUpdater.Apply(args);
            return;
        }
#if DEBUG
        // Rewrites ysfUtil.ico from the drawing in AppIconArt.
        int iconAt = Array.FindIndex(args, a => string.Equals(a, "--makeicon", StringComparison.OrdinalIgnoreCase));
        if (iconAt >= 0 && iconAt + 1 < args.Length)
        {
            Ui.AppIconFile.Write(args[iconAt + 1]);
            return;
        }

        // Renders the flyout to a PNG without putting it on screen:
        // --render <file.png> [light|dark] [settings|audio-settings|modules|timer|timer-running]
        int renderAt = Array.FindIndex(args, a => string.Equals(a, "--render", StringComparison.OrdinalIgnoreCase));
        if (renderAt >= 0 && renderAt + 1 < args.Length)
        {
            Render(args, args[renderAt + 1]);
            return;
        }
#endif

        bool quiet = args.Any(a => string.Equals(a, Autostart.QuietArgument, StringComparison.OrdinalIgnoreCase));

        // Already running? Then that instance opens its flyout and this one exits.
        if (!SingleInstance.TryAcquire())
        {
            if (!quiet)
            {
                SingleInstance.SignalExistingInstance();
            }
            return;
        }

        AppSettings settings = AppSettings.Load();
        _ = Updates.PortableUpdater.CleanupAsync(args);
        System.Windows.Application application = WpfHost.Ensure(settings.Theme);
        application.DispatcherUnhandledException += (_, e) =>
        {
            System.Windows.MessageBox.Show(
                "Unexpected error: " + e.Exception.Message,
                "ysfUtil",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            e.Handled = true;
        };

        using (new AppController(settings, quiet, args.Contains("--update-failed"), args.Contains("--update-show")))
        {
            application.Run();
        }

        SingleInstance.Release();
    }

#if DEBUG
    private static void Render(string[] args, string path)
    {
        WpfHost.Ensure(ThemeMode.System);
        if (args.Any(a => string.Equals(a, "light", StringComparison.OrdinalIgnoreCase)))
        {
            Ui.Theme.ThemeManager.SetMode(ThemeMode.Light);
        }
        else if (args.Any(a => string.Equals(a, "dark", StringComparison.OrdinalIgnoreCase)))
        {
            Ui.Theme.ThemeManager.SetMode(ThemeMode.Dark);
        }

        AppSettings settings = AppSettings.Load();
        Ui.Views.SectionState.Configure(settings, save: false);
        Ui.Flyout.FlyoutWindow? flyout = null;
        // Preview modules never shut Windows down, enforce a microphone level or save settings.
        using var modules = new Modules.ModuleHost(new Modules.ModuleContext
        {
            Settings = settings,
            IsFlyoutVisible = () => false,
            Banner = () => new Ui.Views.AudioSwitchBanner(),
            ShowHint = (_, _, _) => { },
            ShowSettings = then => flyout?.ShowSettings(then),
            IsPreview = true,
        });
        modules.StartEnabled();
        var tools = modules.Running.OfType<Modules.Tools.ToolsModule>().FirstOrDefault();
        if (args.Any(a => string.Equals(a, "timer-running", StringComparison.OrdinalIgnoreCase)))
        {
            tools?.StartPreviewTimer(30);
        }

        using var previewUpdater = new Ui.ViewModels.UpdateViewModel(settings, () => "Preview", () => true, () => { });
        flyout = new Ui.Flyout.FlyoutWindow(modules, new Ui.ViewModels.SettingsViewModel(settings) { Updater = previewUpdater });
        if (args.Any(a => string.Equals(a, "settings", StringComparison.OrdinalIgnoreCase)))
        {
            flyout.ShowSettings();
        }
        if (args.Any(a => string.Equals(a, "audio-settings", StringComparison.OrdinalIgnoreCase)))
        {
            flyout.Loaded += (_, _) => tools?.ShowAudioSettings();
        }
        if (args.Any(a => string.Equals(a, "modules", StringComparison.OrdinalIgnoreCase)))
        {
            flyout.Loaded += (_, _) => flyout.ShowSettings(flyout.ModuleList.BringIntoView);
        }
        if (args.Any(a => string.Equals(a, "timer", StringComparison.OrdinalIgnoreCase)))
        {
            tools?.ShowShutdownTimer();
        }

        DesignRender.Capture(flyout, path, 360, 0);
    }
#endif
}
