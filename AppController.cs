using System.Windows;
using YsfUtil.Features;
using YsfUtil.Hotkeys;
using YsfUtil.Ui;
using YsfUtil.Ui.Flyout;
using YsfUtil.Ui.ViewModels;
using YsfUtil.Ui.Views;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace YsfUtil;

/// <summary>
/// Hält alles zusammen: Einstellungen, Hotkeys, Infobereich und Flyout. ysfUtil lebt im
/// Infobereich; ein eigenes Fenster gibt es nicht. Das Flyout entsteht erst beim ersten Öffnen -
/// wer ysfUtil nur über Hotkeys nutzt, lädt die Oberfläche nie.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly AppSettings settings;
    private readonly HotkeyHost hotkeys = new();
    private readonly TrayController tray;
    private readonly List<FeatureViewModel> features;
    private readonly SettingsViewModel settingsModel;
    private FlyoutWindow? flyout;

    public AppController(AppSettings settings, bool startQuietly)
    {
        this.settings = settings;

        features = FeatureCatalog.All
            .Select(f => new FeatureViewModel(f, settings.For(f)))
            .ToList();
        foreach (FeatureViewModel feature in features)
        {
            feature.Changed += OnFeatureChanged;
            feature.Run += Run;
        }
        settingsModel = new SettingsViewModel(settings);

        tray = new TrayController(features);
        tray.FlyoutToggleRequested += anchor => Flyout().Toggle(anchor);
        tray.FlyoutRequested += showSettings =>
            Flyout().ShowFlyout(TrayAnchor.Resolve(tray.GetIconBounds()), showSettings, activate: true);
        tray.RunRequested += Run;
        tray.AutostartToggled += () => settingsModel.AutostartEnabled = !Autostart.IsEnabled();
        tray.ExitRequested += Exit;

        // Ein zweiter Start von Hand öffnet das Flyout der laufenden Instanz.
        hotkeys.ShowWindowRequested += () =>
            Flyout().ShowFlyout(TrayAnchor.Resolve(tray.GetIconBounds()), activate: true);

        // Während ein Hotkey-Feld aufnimmt, ruhen die angemeldeten Kombinationen - sonst
        // fängt Windows sie ab, bevor das Feld sie sieht.
        HotkeyBox.RecordingChanged += recording =>
        {
            if (recording)
            {
                hotkeys.UnregisterAll();
            }
            else
            {
                ApplyHotkeys();
            }
        };

        ApplyHotkeys();
        Autostart.Refresh();

        bool firstRun = settings.IsFirstRun;
        if (firstRun)
        {
            settings.Save();
        }

        // Von Hand gestartet: kurz zeigen, wo ysfUtil jetzt wohnt. Beim ersten Mal ausführlich,
        // weil Windows neue Symbole gern im Überlauf (^) versteckt.
        if (!startQuietly)
        {
            FeatureViewModel? first = features.FirstOrDefault(f => f.IsEnabled && f.Hotkey != null);
            tray.ShowHint("ysfUtil läuft im Infobereich",
                (firstRun ? "Klick aufs Symbol öffnet ysfUtil. Tipp: Symbol aus dem Überlauf (^) auf die Taskleiste ziehen.\n" : "")
                + (first != null ? $"{first.Hotkey!.Describe()}: {first.Name}" : ""));
        }
    }

    private FlyoutWindow Flyout()
    {
        if (flyout == null)
        {
            flyout = new FlyoutWindow(features, settingsModel);
            flyout.ExitRequested += Exit;

            // Der Zustand kann sich außerhalb geändert haben, etwa in den Windows-Einstellungen.
            flyout.Opening += () =>
            {
                features.ForEach(f => f.Refresh());
                settingsModel.Refresh();
            };
        }
        return flyout;
    }

    private void OnFeatureChanged()
    {
        settings.Save();
        ApplyHotkeys();
    }

    private void ApplyHotkeys()
    {
        hotkeys.UnregisterAll();
        var taken = new Dictionary<Hotkey, FeatureViewModel>();

        foreach (FeatureViewModel feature in features)
        {
            feature.HotkeyError = null;
            if (!feature.IsEnabled || feature.Hotkey is not Hotkey hotkey)
            {
                continue;
            }

            if (taken.TryGetValue(hotkey, out FeatureViewModel? other))
            {
                feature.HotkeyError = $"{hotkey.Describe()} ist schon für „{other.Name}“ vergeben";
                continue;
            }

            FeatureViewModel target = feature;
            if (hotkeys.Register(hotkey, () => Run(target)))
            {
                taken[hotkey] = feature;
            }
            else
            {
                feature.HotkeyError = $"{hotkey.Describe()} wird schon von einem anderen Programm verwendet";
            }
        }
    }

    private void Run(FeatureViewModel feature)
    {
        try
        {
            feature.Feature.Execute();
        }
        catch (Exception ex)
        {
            tray.ShowHint(feature.Name, ex.Message, ToolTipIcon.Error);
        }
        feature.Refresh();
    }

    private void Exit()
    {
        flyout?.Close();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        hotkeys.Dispose();
        tray.Dispose();
    }
}
