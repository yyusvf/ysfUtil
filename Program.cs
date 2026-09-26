using YsfUtil.Ui.Shell;

namespace YsfUtil;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
#if DEBUG
        // Schreibt ysfUtil.ico neu aus der Zeichnung in AppIconArt.
        int iconAt = Array.FindIndex(args, a => string.Equals(a, "--makeicon", StringComparison.OrdinalIgnoreCase));
        if (iconAt >= 0 && iconAt + 1 < args.Length)
        {
            Ui.AppIconFile.Write(args[iconAt + 1]);
            return;
        }

        // Zeichnet das Fenster in eine PNG-Datei, ohne es auf den Bildschirm zu legen:
        // --render <datei.png> [light|dark] [settings]
        int renderAt = Array.FindIndex(args, a => string.Equals(a, "--render", StringComparison.OrdinalIgnoreCase));
        if (renderAt >= 0 && renderAt + 1 < args.Length)
        {
            Render(args, args[renderAt + 1]);
            return;
        }
#endif

        bool quiet = args.Any(a => string.Equals(a, Autostart.QuietArgument, StringComparison.OrdinalIgnoreCase));

        // Läuft schon eine Instanz? Dann holt die ihr Fenster nach vorn, und diese hier geht.
        if (!SingleInstance.TryAcquire())
        {
            if (!quiet)
            {
                SingleInstance.SignalExistingInstance();
            }
            return;
        }

        AppSettings settings = AppSettings.Load();
        System.Windows.Application application = WpfHost.Ensure(settings.Theme);
        application.DispatcherUnhandledException += (_, e) =>
        {
            System.Windows.MessageBox.Show(
                "Unerwarteter Fehler: " + e.Exception.Message,
                "ysfUtil",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            e.Handled = true;
        };

        using (new AppController(settings, quiet))
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
        var features = Features.FeatureCatalog.All
            .Select(f => new Ui.ViewModels.FeatureViewModel(f, settings.For(f)))
            .ToList();

        var flyout = new Ui.Flyout.FlyoutWindow(features, new Ui.ViewModels.SettingsViewModel(settings));
        if (args.Any(a => string.Equals(a, "settings", StringComparison.OrdinalIgnoreCase)))
        {
            flyout.ShowSettingsView();
        }

        DesignRender.Capture(flyout, path, 360, 0);
    }
#endif
}
