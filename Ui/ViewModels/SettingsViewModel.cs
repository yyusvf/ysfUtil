using System.Diagnostics;
using System.IO;
using System.Reflection;
using YsfUtil.Ui.Theme;

namespace YsfUtil.Ui.ViewModels;

internal sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings settings;
    private bool autostart;

    public SettingsViewModel(AppSettings settings)
    {
        this.settings = settings;
        autostart = Autostart.IsEnabled();
        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);
    }

    public bool AutostartEnabled
    {
        get => autostart;
        set
        {
            try
            {
                Autostart.SetEnabled(value);
            }
            catch
            {
                // Nicht schreibbar - der Haken zeigt unten wieder den echten Stand.
            }
            Set(ref autostart, Autostart.IsEnabled());
        }
    }

    /// <summary>Wird beim Öffnen des Fensters neu gelesen - der Eintrag lässt sich auch im Tray-Menü ändern.</summary>
    public void Refresh()
    {
        autostart = Autostart.IsEnabled();
        Raise(nameof(AutostartEnabled));
    }

    public IReadOnlyList<string> Themes { get; } = ["Wie Windows", "Hell", "Dunkel"];

    public int ThemeIndex
    {
        get => (int)settings.Theme;
        set
        {
            var mode = (ThemeMode)value;
            if (settings.Theme == mode)
            {
                return;
            }
            settings.Theme = mode;
            settings.Save();
            ThemeManager.SetMode(mode);
            Raise();
        }
    }

    public string Version { get; } =
        "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

    public RelayCommand OpenSettingsFolderCommand { get; }

    private static void OpenSettingsFolder()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ysfUtil");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
