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
/// Ties everything together: settings, hotkeys, tray icon and flyout. ysfUtil lives in the
/// notification area and has no window of its own. The flyout is only created on first use,
/// so hotkey-only use never loads the UI.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly AppSettings settings;
    private readonly HotkeyHost hotkeys = new();
    private readonly TrayController tray;
    private readonly List<FeatureViewModel> features;
    private readonly SettingsViewModel settingsModel;
    private readonly ShutdownTimer shutdownTimer = new();
    private readonly ShutdownTimerViewModel shutdownModel;
    private readonly System.Windows.Threading.DispatcherTimer shutdownTicker =
        new() { Interval = TimeSpan.FromSeconds(1) };
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
        shutdownModel = new ShutdownTimerViewModel(shutdownTimer, settings);

        tray = new TrayController();
        shutdownTimer.EndingSoon += () => tray.ShowHint("Shutdown in less than a minute",
            "Save your work. Open ysfUtil and click Cancel shutdown to stop the timer.", ToolTipIcon.Warning);
        shutdownTimer.Failed += message => tray.ShowHint("Shutdown timer", message, ToolTipIcon.Error);
        shutdownTimer.Changed += () =>
        {
            if (shutdownTimer.IsRunning) shutdownTicker.Start();
            else shutdownTicker.Stop();
        };
        shutdownTicker.Tick += (_, _) => shutdownTimer.Tick();
        tray.FlyoutToggleRequested += anchor => Flyout().Toggle(anchor);
        tray.FlyoutRequested += anchor => Flyout().ShowFlyout(anchor);

        // Starting the exe again opens the flyout of the running instance.
        hotkeys.ShowWindowRequested += () =>
            Flyout().ShowFlyout(TrayAnchor.Resolve(tray.GetIconBounds()), activate: true);

        // While a hotkey box is recording, registered hotkeys are paused - otherwise Windows
        // would swallow the combination before the box sees it.
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

        // Started by hand: briefly point out where ysfUtil lives. On first run with a tip,
        // since Windows likes to hide new icons in the overflow (^).
        if (!startQuietly)
        {
            FeatureViewModel? first = features.FirstOrDefault(f => f.IsEnabled && f.Hotkey != null);
            tray.ShowHint("ysfUtil is running in the notification area",
                (firstRun ? "Click the icon to open ysfUtil. Tip: drag it from the overflow (^) onto the taskbar.\n" : "")
                + (first != null ? $"{first.Hotkey!.Describe()}: {first.Name}" : ""));
        }
    }

    private FlyoutWindow Flyout()
    {
        if (flyout == null)
        {
            flyout = new FlyoutWindow(features, settingsModel, shutdownModel);
            flyout.ExitRequested += Exit;

            // State may have changed elsewhere, e.g. in the Windows settings.
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
                feature.HotkeyError = $"{hotkey.Describe()} is already used by “{other.Name}”";
                continue;
            }

            FeatureViewModel target = feature;
            if (hotkeys.Register(hotkey, () => Run(target)))
            {
                taken[hotkey] = feature;
            }
            else
            {
                feature.HotkeyError = $"{hotkey.Describe()} is already used by another app";
            }
        }
    }

    private void Run(FeatureViewModel feature)
    {
        if (!feature.IsEnabled) return;
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
        shutdownTicker.Stop();
        shutdownTimer.Cancel();
        hotkeys.Dispose();
        tray.Dispose();
    }
}
