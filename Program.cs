using YsfUtil.Ui.Shell;

namespace YsfUtil;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // The DS5 bridge runs in a child process of ysfUtil, see Modules.DS5.BridgeProcess.
        if (args.Contains(Modules.DS5.BridgeProcess.Argument))
        {
            Environment.ExitCode = Modules.DS5.BridgeProcess.RunChild(args);
            return;
        }
        // Elevated helpers started by the DS5 module's driver setup.
        if (args.Contains(Modules.DS5.Dependencies.HidHideSetupArgument))
        {
            Environment.ExitCode = Modules.DS5.Dependencies.RunElevatedHidHideSetup();
            return;
        }
        if (args.Contains(Modules.DS5.Dependencies.HidHideAppsArgument))
        {
            Environment.ExitCode = Modules.DS5.Dependencies.RunElevatedHidHideApps(args);
            return;
        }
        if (args.Contains(Modules.DS5.Dependencies.HidHideUndoArgument))
        {
            Environment.ExitCode = Modules.DS5.Dependencies.RunElevatedHidHideUndo();
            return;
        }
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
        // --render <file.png> [light|dark] [settings|audio-settings|modules|timer|timer-running] [all-modules] [module:<id>|module-settings:<id>]
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
        if (args.Any(a => string.Equals(a, "all-modules", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var module in Modules.ModuleCatalog.All) settings.For(module).Enabled = true;
        }
        Ui.Flyout.FlyoutWindow? flyout = null;
        // Preview modules never shut Windows down, enforce a microphone level or save settings.
        using var modules = new Modules.ModuleHost(new Modules.ModuleContext
        {
            Settings = settings,
            IsFlyoutVisible = () => false,
            Banner = () => new Ui.Views.AudioSwitchBanner(),
            ShowHint = (_, _, _) => { },
            ShowSettings = then => flyout?.ShowSettings(then),
            PickFile = _ => null,
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
        // module:<id> scrolls to that module's card, module-settings:<id> to its settings.
        foreach (string arg in args)
        {
            string[] parts = arg.Split(':', 2);
            if (parts.Length < 2 || modules.Find(parts[1]) is not { } module) continue;
            if (parts[0] == "module") flyout.Loaded += (_, _) => module.FlyoutCards.FirstOrDefault()?.View.BringIntoView();
            if (parts[0] == "module-settings") flyout.Loaded += (_, _) => flyout.ShowSettings(() => module.SettingsCards.LastOrDefault()?.View.BringIntoView());
        }

        // tall: the whole content, without the flyout's height limit.
        if (args.Any(a => string.Equals(a, "tall", StringComparison.OrdinalIgnoreCase))) flyout.MaxHeight = 2400;
        DesignRender.Capture(flyout, path, (int)flyout.Width, 0);
    }
#endif
}
