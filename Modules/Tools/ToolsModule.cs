using System.Windows;
using System.Windows.Data;
using YsfUtil.Audio;
using YsfUtil.Features;
using YsfUtil.Ui.ViewModels;
using YsfUtil.Ui.Views;
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
    private readonly ShutdownTimerCard shutdownCard;
    private readonly AudioOutputSettingsCard audioSettingsCard;
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

        var audioCard = VisibleWhen(new AudioOutputCard { DataContext = audioModel }, "Feature.IsEnabled");
        audioCard.SettingsRequested += ShowAudioSettings;
        shutdownCard = VisibleWhen(new ShutdownTimerCard { DataContext = shutdownModel }, nameof(ShutdownTimerViewModel.IsEnabled));
        audioSettingsCard = new AudioOutputSettingsCard { DataContext = audioModel };
        var toggles = features.Where(f => !f.IsAudioOutput).ToList();
        FlyoutCards =
        [
            new(ModuleCategory.Audio, 0, audioCard),
            new(ModuleCategory.Audio, 10, VisibleWhen(new AudioVolumeCard { DataContext = volumeModel }, nameof(AudioVolumeViewModel.IsEnabled))),
            new(ModuleCategory.Desktop, 0, new ToolsFlyoutView(toggles), () => toggles.Any(f => f.IsEnabled)),
            new(ModuleCategory.System, 0, shutdownCard),
        ];
        SettingsCards =
        [
            new(ModuleCategory.Modules, 0, new ToolsSettingsView(features, shutdownModel, volumeModel)),
            new(ModuleCategory.Audio, 0, audioSettingsCard),
        ];
    }

    private static T VisibleWhen<T>(T view, string path) where T : FrameworkElement
    {
        view.SetBinding(UIElement.VisibilityProperty, new Binding(path) { Converter = new System.Windows.Controls.BooleanToVisibilityConverter() });
        return view;
    }

    public IReadOnlyList<FeatureViewModel> Features => features;

    public IReadOnlyList<ModuleCard> FlyoutCards { get; }

    public IReadOnlyList<ModuleCard> SettingsCards { get; }

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
    public void ShowAudioSettings() => context.ShowSettings(() => Reveal(audioSettingsCard.ExpandOptions, audioSettingsCard));

    /// <summary>For the preview: open the shutdown timer's options.</summary>
    public void ShowShutdownTimer() => Reveal(shutdownCard.ExpandOptions, shutdownCard);

    private static void Reveal(Action expand, FrameworkElement card)
    {
        expand();
        card.UpdateLayout();
        card.BringIntoView();
    }

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
