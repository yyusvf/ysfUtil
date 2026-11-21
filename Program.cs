using System.IO;
using YsfUtil.Ui.Shell;

namespace YsfUtil;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // A plugin's own process (e.g. the DS5 bridge), see IModuleContext.CreateProcess.
        if (Plugins.PluginProcess.IsRequested(args))
        {
            Environment.ExitCode = Plugins.PluginProcess.Run(args);
            return;
        }
        // ysfUtil.exe --install-plugin <file.ysfplugin.zip>: installs a plugin from disk, e.g. a local build.
        int installAt = Array.IndexOf(args, "--install-plugin");
        if (installAt >= 0 && installAt + 1 < args.Length)
        {
            Environment.ExitCode = InstallPlugin(args[installAt + 1], silent: args.Contains("--silent"));
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

        // A restart (after a plugin update) waits until the old instance has gone.
        Plugins.PluginManager.WaitForPrevious(args);

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

    private static int InstallPlugin(string path, bool silent)
    {
        try
        {
            Plugins.InstalledPlugin plugin = Plugins.PluginStore.Default.Install(path);
            if (!silent) System.Windows.MessageBox.Show($"{plugin.Manifest.Name} {plugin.Manifest.Version} is installed. Restart ysfUtil to use it.",
                "ysfUtil", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (!silent) System.Windows.MessageBox.Show("The plugin could not be installed: " + e.Message,
                "ysfUtil", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return 1;
        }
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
        Ui.Views.SectionState.Configure(settings.CollapsedSections, null);
        if (args.Any(a => string.Equals(a, "all-modules", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var key in settings.Modules.Keys.Concat(Plugins.PluginStore.Default.Installed().Select(p => p.Id)).ToList())
                settings.Modules[key] = new Modules.ModuleSettings { Enabled = true };
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
        }, Modules.ModuleCatalog.All.Concat(Plugins.PluginManager.Modules(Plugins.PluginStore.Default)));
        modules.Failed += (name, message) => Console.Error.WriteLine($"{name}: {message}");
        modules.StartEnabled();
        var tools = modules.Running.OfType<Modules.Tools.ToolsModule>().FirstOrDefault();
        if (args.Any(a => string.Equals(a, "timer-running", StringComparison.OrdinalIgnoreCase)))
        {
            tools?.StartPreviewTimer(30);
        }

        using var previewPlugins = new Plugins.PluginManager(modules, Plugins.PluginStore.Default, settings,
            (_, _, _) => { }, () => "Preview", () => { });
        using var previewUpdater = new Ui.ViewModels.UpdateViewModel(settings, () => "Preview", () => true, () => { });
        flyout = new Ui.Flyout.FlyoutWindow(modules, new Ui.ViewModels.SettingsViewModel(settings) { Updater = previewUpdater, Plugins = previewPlugins });
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
