using System.Windows;
using YsfUtil.Features;
using YsfUtil.Audio;
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
    private readonly WindowsAudioOutputBackend audioBackend = new();
    private readonly AudioOutputSwitcher audio;
    private readonly AudioOutputViewModel audioModel;
    private readonly System.Windows.Threading.DispatcherTimer audioRefresh =
        new() { Interval = TimeSpan.FromMilliseconds(150) };
    private Ui.Views.AudioSwitchBanner? audioBanner;
    private bool disposed;
    private readonly WindowsAudioVolumeBackend volumeBackend = new();
    private readonly AudioVolumeViewModel volumeModel;
    private readonly System.Windows.Threading.DispatcherTimer volumeWatch =
        new() { Interval = TimeSpan.FromSeconds(1) };
    private int volumeRefreshQueued;
    private readonly UpdateViewModel updater;

    public AppController(AppSettings settings, bool startQuietly, bool updateFailed = false, bool showAfterUpdate = false)
    {
        this.settings = settings;
        SectionState.Configure(settings);
        audio = new AudioOutputSwitcher(audioBackend, settings.AudioOutput);

        features = FeatureCatalog.Create(audio)
            .Select(f => new FeatureViewModel(f, settings.For(f)))
            .ToList();
        foreach (FeatureViewModel feature in features)
        {
            feature.Changed += OnFeatureChanged;
            feature.Run += Run;
        }
        settingsModel = new SettingsViewModel(settings);
        settingsModel.PreviewNotificationRequested += () => Banner().ShowPreview();
        updater = new UpdateViewModel(settings,
            () => shutdownTimer.IsRunning ? "Cancel the active shutdown timer before updating." : null,
            () => flyout?.IsVisible != true, Exit);
        settingsModel.Updater = updater;
        shutdownModel = new ShutdownTimerViewModel(shutdownTimer, settings);
        audioModel = new AudioOutputViewModel(audio,
            features.Single(f => f.IsAudioOutput), settings.Save);
        var volume = new AudioVolumeController(volumeBackend, settings.AudioVolume);
        volumeModel = new AudioVolumeViewModel(volume, settings.Save);
        void UpdateVolumeWatch()
        {
            var interval = TimeSpan.FromMilliseconds(settings.AudioVolume.LockMicrophone ? 250 : 1000);
            if (volumeWatch.Interval != interval) volumeWatch.Interval = interval;
            if (!settings.AudioVolume.Enabled) volumeWatch.Stop();
            else if (!volumeWatch.IsEnabled) volumeWatch.Start();
        }
        volume.Changed += UpdateVolumeWatch;
        volumeWatch.Tick += (_, _) =>
        {
            if (flyout?.IsVisible == true || settings.AudioVolume.LockMicrophone) volumeModel.Refresh();
        };
        UpdateVolumeWatch();
        var dispatcher = Application.Current.Dispatcher;
        volumeBackend.VolumeChanged += () =>
        {
            if (disposed || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref volumeRefreshQueued, 1) != 0) return;
            dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref volumeRefreshQueued, 0);
                if (!disposed && (flyout?.IsVisible == true || settings.AudioVolume.LockMicrophone)) volumeModel.Refresh();
            });
        };
        audioBackend.DevicesChanged += () =>
        {
            if (disposed || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(() =>
            {
                if (disposed) return;
                audioRefresh.Stop();
                audioRefresh.Start();
            });
        };
        audioRefresh.Tick += (_, _) => { audioRefresh.Stop(); audioModel.Refresh(); volumeModel.Refresh(); };
        audio.Switched += device =>
        {
            if (settings.AudioOutput.ShowBanner)
                Banner().ShowDevice(device);
        };
        audio.Failed += message =>
        {
            if (!disposed) Banner().ShowError(message);
        };

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
            flyout = new FlyoutWindow(features, settingsModel, shutdownModel, audioModel, volumeModel);
            flyout.ExitRequested += Exit;

            // State may have changed elsewhere, e.g. in the Windows settings.
            flyout.Opening += () =>
            {
                features.ForEach(f => f.Refresh());
                settingsModel.Refresh();
                // Paint the panel before querying Windows audio devices, which can be slow
                // with Bluetooth/virtual drivers. Refresh values once it is on screen.
                Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    if (disposed || flyout?.IsVisible != true) return;
                    audioModel.Refresh();
                    volumeModel.Refresh();
                }));
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
        audioRefresh.Stop();
        volumeWatch.Stop();
        volumeBackend.Dispose();
        audioBanner?.Close();
        audioBackend.Dispose();
        shutdownTicker.Stop();
        shutdownTimer.Cancel();
        hotkeys.Dispose();
        tray.Dispose();
    }
}
