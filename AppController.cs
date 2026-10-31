using System.Windows;
using YsfUtil.Hotkeys;
using YsfUtil.Modules;
using YsfUtil.Ui;
using YsfUtil.Ui.Flyout;
using YsfUtil.Ui.ViewModels;
using YsfUtil.Ui.Views;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace YsfUtil;

/// <summary>
/// Ties everything together: settings, modules, hotkeys, tray icon and flyout. ysfUtil lives in
/// the notification area and has no window of its own. The flyout is only created on first use,
/// so hotkey-only use never loads the UI. What the app actually does lives in modules, see
/// <see cref="ModuleCatalog"/>.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly AppSettings settings;
    private readonly HotkeyHost hotkeys = new();
    private readonly TrayController tray;
    private readonly ModuleHost modules;
    private readonly SettingsViewModel settingsModel;
    private FlyoutWindow? flyout;
    private AudioSwitchBanner? audioBanner;
    private bool disposed;
    private readonly UpdateViewModel updater;

    public AppController(AppSettings settings, bool startQuietly, bool updateFailed = false, bool showAfterUpdate = false)
    {
        this.settings = settings;
        SectionState.Configure(settings);
        tray = new TrayController();

        modules = new ModuleHost(new ModuleContext
        {
            Settings = settings,
            IsFlyoutVisible = () => flyout?.IsVisible == true,
            Banner = Banner,
            ShowHint = tray.ShowHint,
            ShowSettings = then => Flyout().ShowSettings(then),
        });
        modules.Failed += (name, message) => tray.ShowHint(name, message, ToolTipIcon.Error);
        modules.Changed += WireFeatures;
        modules.StartEnabled();

        settingsModel = new SettingsViewModel(settings);
        settingsModel.PreviewNotificationRequested += () => Banner().ShowPreview();
        updater = new UpdateViewModel(settings, () => modules.UpdateBlocker,
            () => flyout?.IsVisible != true, Exit);
        settingsModel.Updater = updater;

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

        Autostart.Refresh();
        // Start after Application.Run installs WPF's synchronization context, so update
        // continuations (including an automatic restart) stay on the owning UI thread.
        Application.Current.Dispatcher.BeginInvoke(new Action(async () => await updater.StartAsync(updateFailed)));
        if (showAfterUpdate)
            Application.Current.Dispatcher.BeginInvoke(() => Flyout().ShowFlyout(TrayAnchor.Resolve(tray.GetIconBounds()), activate: true));

        bool firstRun = settings.IsFirstRun;
        if (firstRun)
        {
            settings.Save();
        }

        // Same passive, click-through overlay as audio switching; quiet startup stays silent.
        if (!startQuietly)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!disposed) Banner().ShowStartup(firstRun);
            });
        }
    }

    private AudioSwitchBanner Banner()
    {
        var banner = audioBanner ??= new AudioSwitchBanner();
        banner.Position = settings.NotificationPosition;
        return banner;
    }

    private FlyoutWindow Flyout()
    {
        if (flyout == null)
        {
            flyout = new FlyoutWindow(modules, settingsModel);
            flyout.ExitRequested += Exit;

            // State may have changed elsewhere, e.g. in the Windows settings.
            flyout.Opening += () =>
            {
                settingsModel.Refresh();
                foreach (IModule module in modules.Running) module.OnFlyoutOpening();
            };
        }
        return flyout;
    }

    /// <summary>A module started or stopped: hook up its features and refresh the hotkeys.</summary>
    private void WireFeatures()
    {
        foreach (FeatureViewModel feature in modules.Features)
        {
            // Re-wiring a feature that was already running must not subscribe twice.
            feature.Changed -= OnFeatureChanged;
            feature.Changed += OnFeatureChanged;
            feature.Run -= Run;
            feature.Run += Run;
        }
        ApplyHotkeys();
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

        foreach (FeatureViewModel feature in modules.Features)
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

    private async void Run(FeatureViewModel feature)
    {
        if (!feature.IsEnabled) return;
        try
        {
            await feature.Feature.ExecuteAsync();
        }
        catch (Exception ex)
        {
            // Audio errors already appear in the passive overlay and inline in the flyout.
            if (!feature.IsAudioOutput) tray.ShowHint(feature.Name, ex.Message, ToolTipIcon.Error);
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
        disposed = true;
        updater.Dispose();
        modules.Dispose();
        audioBanner?.Close();
        hotkeys.Dispose();
        tray.Dispose();
    }
}
