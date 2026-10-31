using System.Windows;
using YsfUtil.Audio;
using YsfUtil.Features;
using YsfUtil.Ui.ViewModels;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace YsfUtil.Modules.Tools;

/// <summary>
/// ysfUtil's original helpers: taskbar and desktop icon toggles, audio output switching, volume
/// controls and the shutdown timer. Each can still be switched off on its own in its settings.
/// </summary>
internal sealed class ToolsModule : IModule
{
    private readonly ModuleContext context;
    private readonly List<FeatureViewModel> features;
    private readonly ShutdownTimer shutdownTimer;
    private readonly ShutdownTimerViewModel shutdownModel;
    private readonly System.Windows.Threading.DispatcherTimer shutdownTicker =
        new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly WindowsAudioOutputBackend audioBackend = new();
    private readonly AudioOutputViewModel audioModel;
    private readonly System.Windows.Threading.DispatcherTimer audioRefresh =
        new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly WindowsAudioVolumeBackend volumeBackend = new();
    private readonly AudioVolumeViewModel volumeModel;
    private readonly System.Windows.Threading.DispatcherTimer volumeWatch =
        new() { Interval = TimeSpan.FromSeconds(1) };
    private int volumeRefreshQueued;
    private readonly ToolsFlyoutView flyoutView;
    private readonly ToolsSettingsView settingsView;
    private bool disposed;

    public ToolsModule(ModuleContext context)
    {
        this.context = context;
        AppSettings settings = context.Settings;
        var audio = new AudioOutputSwitcher(audioBackend, settings.AudioOutput);

        features = FeatureCatalog.Create(audio)
            .Select(f => new FeatureViewModel(f, settings.For(f)))
            .ToList();
        foreach (FeatureViewModel feature in features)
        {
            feature.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FeatureViewModel.IsEnabled)) ContentChanged?.Invoke();
            };
        }

        // Preview timers never invoke Windows shutdown.
        shutdownTimer = context.IsPreview ? new ShutdownTimer(() => { }) : new ShutdownTimer();
        shutdownModel = new ShutdownTimerViewModel(shutdownTimer, settings);
        shutdownModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShutdownTimerViewModel.IsEnabled)) ContentChanged?.Invoke();
        };
        shutdownTimer.EndingSoon += () => context.ShowHint("Shutdown in less than a minute",
            "Save your work. Open ysfUtil and click Cancel shutdown to stop the timer.", ToolTipIcon.Warning);
        shutdownTimer.Failed += message => context.ShowHint("Shutdown timer", message, ToolTipIcon.Error);
        shutdownTimer.Changed += () =>
        {
            if (shutdownTimer.IsRunning) shutdownTicker.Start();
            else shutdownTicker.Stop();
        };
        shutdownTicker.Tick += (_, _) => shutdownTimer.Tick();

        Action save = context.IsPreview ? () => { } : settings.Save;
        audioModel = new AudioOutputViewModel(audio, features.Single(f => f.IsAudioOutput), save);

        // Preview rendering reads levels but never enforces or saves a microphone setting.
        var volume = new AudioVolumeController(volumeBackend, context.IsPreview
            ? new AudioVolumeSettings { Enabled = settings.AudioVolume.Enabled, MicrophoneTarget = settings.AudioVolume.MicrophoneTarget }
            : settings.AudioVolume);
        volumeModel = new AudioVolumeViewModel(volume, save);
        volumeModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AudioVolumeViewModel.IsEnabled)) ContentChanged?.Invoke();
        };
        void UpdateVolumeWatch()
        {
            var interval = TimeSpan.FromMilliseconds(settings.AudioVolume.LockMicrophone ? 250 : 1000);
            if (volumeWatch.Interval != interval) volumeWatch.Interval = interval;
            if (!settings.AudioVolume.Enabled || context.IsPreview) volumeWatch.Stop();
            else if (!volumeWatch.IsEnabled) volumeWatch.Start();
        }
        volume.Changed += UpdateVolumeWatch;
        volumeWatch.Tick += (_, _) =>
        {
            if (context.IsFlyoutVisible() || settings.AudioVolume.LockMicrophone) volumeModel.Refresh();
        };
        UpdateVolumeWatch();

        var dispatcher = Application.Current.Dispatcher;
        volumeBackend.VolumeChanged += () =>
        {
            if (disposed || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref volumeRefreshQueued, 1) != 0) return;
            dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref volumeRefreshQueued, 0);
                if (!disposed && (context.IsFlyoutVisible() || settings.AudioVolume.LockMicrophone)) volumeModel.Refresh();
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
                context.Banner().ShowDevice(device);
        };
        audio.Failed += message =>
        {
            if (!disposed) context.Banner().ShowError(message);
        };

        flyoutView = new ToolsFlyoutView(features, shutdownModel, audioModel, volumeModel);
        settingsView = new ToolsSettingsView(features, shutdownModel, audioModel, volumeModel);
        flyoutView.AudioSettingsRequested += ShowAudioSettings;
    }

    public IReadOnlyList<FeatureViewModel> Features => features;

    public FrameworkElement FlyoutView => flyoutView;

    public FrameworkElement SettingsView => settingsView;

    public bool HasVisibleContent => features.Any(f => f.IsEnabled) || shutdownModel.IsEnabled || volumeModel.IsEnabled;

    public event Action? ContentChanged;

    public string? UpdateBlocker =>
        shutdownTimer.IsRunning ? "Cancel the active shutdown timer before updating." : null;

    public void OnFlyoutOpening()
    {
        features.ForEach(f => f.Refresh());
        // Paint the panel before querying Windows audio devices, which can be slow with
        // Bluetooth/virtual drivers. Refresh values once it is on screen.
        Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (disposed || !context.IsFlyoutVisible()) return;
            audioModel.Refresh();
            volumeModel.Refresh();
        }));
    }

    /// <summary>Opens Settings at the audio output section.</summary>
    public void ShowAudioSettings() => context.ShowSettings(settingsView.ShowAudioSettings);

    /// <summary>For the preview: open the shutdown timer's options.</summary>
    public void ShowShutdownTimer() => flyoutView.ShowShutdownTimer();

    /// <summary>For the preview: start a countdown that never shuts Windows down.</summary>
    public void StartPreviewTimer(int minutes)
    {
        if (context.IsPreview) shutdownTimer.Start(minutes);
    }

    public void Dispose()
    {
        disposed = true;
        audioRefresh.Stop();
        volumeWatch.Stop();
        volumeBackend.Dispose();
        audioBackend.Dispose();
        shutdownTicker.Stop();
        shutdownTimer.Cancel();
    }
}
