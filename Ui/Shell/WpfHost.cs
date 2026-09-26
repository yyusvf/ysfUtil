using Application = System.Windows.Application;
using ResourceDictionary = System.Windows.ResourceDictionary;
using ShutdownMode = System.Windows.ShutdownMode;

namespace YsfUtil.Ui.Shell;

/// <summary>
/// Erzeugt die WPF-Anwendung von Hand, weil <see cref="Program"/> der Einstiegspunkt bleibt.
/// </summary>
internal static class WpfHost
{
    private static Application? application;

    public static Application Ensure(ThemeMode theme = ThemeMode.System)
    {
        if (application != null)
        {
            return application;
        }

        application = new Application
        {
            // Das Fenster schließt in den Infobereich. Beendet wird nur über das Tray-Menü.
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        // Maße vorn, weil die Vorlagen sie über StaticResource holen. Der Farbsatz kommt
        // danach auf seinen festen Platz, siehe ThemeManager.
        foreach (string path in new[]
        {
            "Ui/Theme/Metrics.xaml",
            "Ui/Theme/Controls.xaml",
            "Ui/Theme/Inputs.xaml",
        })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{path}", UriKind.Absolute),
            });
        }

        Theme.SystemAccent.Attach(application);
        Theme.ThemeManager.Attach(application, theme);

        return application;
    }
}
