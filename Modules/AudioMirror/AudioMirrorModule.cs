using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using YsfUtil.Features;
using YsfUtil.Hotkeys;
using YsfUtil.Modules.AudioMirror.Engine;
using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Modules.AudioMirror;

/// <summary>An entry of the source picker. A null id follows the Windows default device.</summary>
internal sealed record SourceItem(string? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Plays Windows audio on several output devices at once - the standalone Audio Mirror as a
/// module. The engine is unchanged; this class takes over what its window controller did: device
/// and app lists, the app mix, status, and the hotkey that turns everything off and back on.
/// </summary>
internal sealed class AudioMirrorModule : ViewModelBase, IModule
{
    /// <summary>The status tick runs every 500 ms; the app list is fetched every fourth tick.</summary>
    private const int AppListEveryTicks = 4;

    private readonly ModuleContext context;
    private readonly AudioMirrorSettings settings;
    private readonly MirrorEngine engine = new();
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer debounceTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly FeatureViewModel toggleFeature;
    private readonly AudioMirrorFlyoutView flyoutView;
    private readonly AudioMirrorSettingsView settingsView;

    /// <summary>
    /// True while lists are rebuilt or set from code. Otherwise every value set there would come
    /// back as user input and be saved again.
    /// </summary>
    private bool suppress;

    private bool disposed;
    private bool needsDeviceRefresh;
    private int statusTicks;
    private string? lastError;
    private SourceItem? selectedSource;
    private string status = string.Empty;
    private bool statusIsError;

    public AudioMirrorModule(ModuleContext context)
    {
        this.context = context;
        if (context.Settings.AudioMirror == null)
        {
            (AudioMirrorSettings imported, Hotkey? hotkey) = AudioMirrorSettings.ImportStandalone();
            context.Settings.AudioMirror = imported;
            var feature = new ToggleMirroringFeature(this);
            context.Settings.For(feature).Hotkey = hotkey?.ToString();
            if (!context.IsPreview) context.Settings.Save();
        }
        settings = context.Settings.AudioMirror;
        settings.Devices = new(settings.Devices ?? [], StringComparer.OrdinalIgnoreCase);

        var toggle = new ToggleMirroringFeature(this);
        toggleFeature = new FeatureViewModel(toggle, context.Settings.For(toggle));
        ToggleCommand = new RelayCommand(ToggleEverything);

        engine.DeviceListChanged += () => Post(ScheduleRefresh);
        engine.DefaultDeviceChanged += () => Post(ScheduleRefresh);
        engine.FixedSourceDeviceId = settings.SourceDeviceId;

        debounceTimer.Tick += (_, _) => OnDebounceTick();
        statusTimer.Tick += (_, _) => OnStatusTick();

        RefreshDevices();
        RefreshAppLists();
        if (!context.IsPreview)
        {
            SyncTargets();
            statusTimer.Start();
        }
        else
        {
            RefreshStatus();
        }

        flyoutView = new AudioMirrorFlyoutView { DataContext = this };
        settingsView = new AudioMirrorSettingsView { DataContext = this };
    }

    // ══ Module ══════════════════════════════════════════════════════════════

    public IReadOnlyList<FeatureViewModel> Features => [toggleFeature];

    public FrameworkElement FlyoutView => flyoutView;

    public FrameworkElement SettingsView => settingsView;

    public bool HasVisibleContent => true;

    public event Action? ContentChanged { add { } remove { } }

    public string? UpdateBlocker => null;

    public void OnFlyoutOpening() => RefreshStatus();

    // ══ Bindings ════════════════════════════════════════════════════════════

    public FeatureViewModel ToggleFeature => toggleFeature;

    public RelayCommand ToggleCommand { get; }

    public ObservableCollection<SourceItem> Sources { get; } = [];

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    public SourceItem? SelectedSource
    {
        get => selectedSource;
        set
        {
            if (!Set(ref selectedSource, value) || suppress) return;
            settings.SourceDeviceId = value?.Id;
            Save();
            engine.FixedSourceDeviceId = settings.SourceDeviceId;
            // The source decides which device is locked and which apps are listed.
            RefreshDevices();
            RefreshAppLists();
            SyncTargets();
        }
    }

    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public bool StatusIsError
    {
        get => statusIsError;
        private set => Set(ref statusIsError, value);
    }

    public bool IsMirroring => Devices.Any(d => d.IsEnabled && !d.IsSource);

    public string ToggleGlyph => IsMirroring ? "" : "";

    public string ToggleTip => IsMirroring ? "Turn mirroring off (remembers the selection)" : "Restore the last mirroring";

    public double BufferMs
    {
        get => Math.Clamp(settings.BufferMs, 10, 250);
        set
        {
            int clamped = (int)Math.Clamp(Math.Round(value / 5) * 5, 10, 250);
            if (clamped == settings.BufferMs) return;
            settings.BufferMs = clamped;
            Save();
            Raise();
            Raise(nameof(BufferText));
            debounceTimer.Stop();
            debounceTimer.Start();
        }
    }

    public string BufferText => $"{BufferMs:0} ms";

    // ══ Devices ═════════════════════════════════════════════════════════════

    private string? EffectiveSourceId => settings.SourceDeviceId ?? engine.TryGetDefaultDeviceId();

    private void RefreshDevices()
    {
        IReadOnlyList<AudioDeviceInfo> devices = engine.ListOutputDevices();
        string? sourceId = EffectiveSourceId;

        suppress = true;
        RefreshSourceList(devices);

        foreach (DeviceViewModel old in Devices)
        {
            old.PropertyChanged -= OnDeviceChanged;
            foreach (AppViewModel app in old.Apps) app.PropertyChanged -= OnAppChanged;
        }
        Devices.Clear();

        // Disconnected devices are not listed, but keep their settings: plugged back in, they
        // return as they were.
        foreach (AudioDeviceInfo device in devices.Where(d => d.Connected))
        {
            var model = new DeviceViewModel(device.Id, device.Name, device.Kind, device.Id == sourceId, device.IconPath);
            DeviceSetting setting = settings.For(device.Id);
            model.Volume = setting.Volume;
            model.IsEnabled = setting.Enabled && device.Id != sourceId;
            model.PropertyChanged += OnDeviceChanged;
            Devices.Add(model);
        }
        suppress = false;
        RaiseToggle();
    }

    private void RefreshSourceList(IReadOnlyList<AudioDeviceInfo> devices)
    {
        string? wanted = settings.SourceDeviceId;
        Sources.Clear();
        Sources.Add(new SourceItem(null, Strings.WindowsDefaultDevice(
            devices.FirstOrDefault(d => d.IsDefault)?.Name ?? Strings.NoDeviceAvailable)));
        foreach (AudioDeviceInfo device in devices.Where(d => d.Connected))
        {
            Sources.Add(new SourceItem(device.Id, device.Name));
        }
        // A fixed source that is unplugged stays listed - otherwise the choice would silently
        // fall back to the default device.
        if (wanted != null && devices.All(d => d.Id != wanted || !d.Connected))
        {
            Sources.Add(new SourceItem(wanted, Strings.LastChosenUnavailable));
        }
        SelectedSource = Sources.FirstOrDefault(i => i.Id == wanted) ?? Sources[0];
    }

    /// <summary>Syncs the app lists. True if the set of apps changed.</summary>
    private bool RefreshAppLists()
    {
        IReadOnlyList<AudioAppInfo> apps = engine.ListAudioApps();
        bool changed = false;

        suppress = true;
        foreach (DeviceViewModel device in Devices)
        {
            bool sameSet = apps.Count == device.Apps.Count
                && apps.All(a => device.Apps.Any(x => x.Key.Equals(a.Key, StringComparison.OrdinalIgnoreCase)));
            if (sameSet) continue;

            changed = true;
            foreach (AppViewModel old in device.Apps) old.PropertyChanged -= OnAppChanged;
            device.Apps.Clear();
            foreach (AudioAppInfo app in apps)
            {
                (bool enabled, float volume) = settings.Lookup(device.DeviceId, app.Key);
                var model = new AppViewModel(app.Key, app.Name) { IsEnabled = enabled, Volume = volume };
                model.PropertyChanged += OnAppChanged;
                device.Apps.Add(model);
            }
        }
        suppress = false;
        return changed;
    }

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (suppress || sender is not DeviceViewModel device) return;
        switch (e.PropertyName)
        {
            case nameof(DeviceViewModel.IsEnabled):
                settings.For(device.DeviceId).Enabled = device.IsEnabled;
                Save();
                SyncTargets();
                RaiseToggle();
                break;
            case nameof(DeviceViewModel.Volume):
                settings.For(device.DeviceId).Volume = device.Volume;
                Save();
                engine.SetMasterVolume(device.DeviceId, device.Volume);
                break;
        }
    }

    private void OnAppChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (suppress || sender is not AppViewModel app) return;
        DeviceViewModel? owner = Devices.FirstOrDefault(d => d.Apps.Contains(app));
        if (owner == null) return;

        AppMixSetting stored = settings.For(owner.DeviceId, app.Key);
        bool wasEnabled = stored.Enabled;
        stored.Enabled = app.IsEnabled;
        stored.Volume = app.Volume;
        Save();

        // A volume change alone needs no new captures.
        if (wasEnabled != app.IsEnabled) SyncTargets();
        else engine.SetTargets(BuildTargets(), settings.BufferMs);
    }

    // ══ Mirroring ═══════════════════════════════════════════════════════════

    /// <summary>Everything on at full volume? Then capturing the whole device is enough.</summary>
    private static bool UsesWholeDevice(DeviceViewModel device) =>
        device.Apps.All(a => a.IsEnabled && a.Volume >= 0.999f);

    private MirrorTarget[] BuildTargets() => Devices
        .Where(d => d.IsEnabled && !d.IsSource)
        .Select(d => new MirrorTarget(
            d.DeviceId,
            d.Volume,
            UsesWholeDevice(d),
            d.Apps.Where(a => a.IsEnabled).Select(a => new MirrorAppTarget(a.Key, a.Volume)).ToList()))
        .ToArray();

    private void SyncTargets()
    {
        if (disposed || context.IsPreview) return;
        try
        {
            engine.SetTargets(BuildTargets(), settings.BufferMs);
            lastError = null;
        }
        catch (Exception ex)
        {
            engine.Stop();
            lastError = ex.Message;
        }
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (disposed) return;
        var byId = engine.Outputs.ToDictionary(o => o.DeviceId);
        int running = 0;
        int failed = 0;

        foreach (DeviceViewModel device in Devices)
        {
            foreach (AppViewModel app in device.Apps)
            {
                app.Status = app.IsEnabled ? engine.GetAppError(app.Key) ?? string.Empty : string.Empty;
            }
            if (device.IsSource || !byId.TryGetValue(device.DeviceId, out DeviceOutput? output))
            {
                device.Status = string.Empty;
                continue;
            }
            if (output.Error != null)
            {
                device.Status = output.Error + Strings.RetryRunning;
                failed++;
            }
            else if (UsesWholeDevice(device))
            {
                device.Status = Strings.RunningWholeSound(output.EstimatedLatencyMs);
                running++;
            }
            else
            {
                int count = output.ActiveAppCount;
                device.Status = count == 0 ? Strings.NoAppSelected : Strings.RunningApps(count, output.EstimatedLatencyMs);
                running++;
            }
        }

        if (lastError != null) { SetStatus(lastError, true); return; }
        if (engine.SourceUnavailable) { SetStatus(Strings.SourceUnavailable, true); return; }

        int selected = Devices.Count(d => d.IsEnabled && !d.IsSource);
        if (selected == 0) { SetStatus(Strings.NothingTicked, false); return; }
        if (running == 0)
        {
            SetStatus(Strings.WaitingFor(engine.WholeDeviceError ?? Strings.DeviceOrApp, selected), true);
            return;
        }
        SetStatus(Strings.MirroringOnDevices(running) + (failed > 0 ? Strings.StillWaiting(failed) : ""), failed > 0);
    }

    private void SetStatus(string text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }

    private void RaiseToggle()
    {
        Raise(nameof(IsMirroring));
        Raise(nameof(ToggleGlyph));
        Raise(nameof(ToggleTip));
    }

    /// <summary>
    /// Turns all mirroring off, or back on. Off remembers exactly which devices were on and how
    /// their apps were mixed; on restores that, skipping devices that are gone meanwhile.
    /// </summary>
    internal void ToggleEverything()
    {
        if (IsMirroring)
        {
            var snapshot = new Dictionary<string, DeviceSetting>(StringComparer.OrdinalIgnoreCase);
            foreach (DeviceViewModel device in Devices.Where(d => d.IsEnabled && !d.IsSource))
            {
                var entry = new DeviceSetting { Enabled = true, Volume = device.Volume };
                foreach (AppViewModel app in device.Apps)
                {
                    entry.Apps[app.Key] = new AppMixSetting { Enabled = app.IsEnabled, Volume = app.Volume };
                }
                snapshot[device.DeviceId] = entry;
            }
            settings.HotkeySnapshot = snapshot;

            suppress = true;
            foreach (DeviceViewModel device in Devices.Where(d => d.IsEnabled && !d.IsSource))
            {
                device.IsEnabled = false;
                settings.For(device.DeviceId).Enabled = false;
            }
            suppress = false;

            Save();
            SyncTargets();
            RaiseToggle();
            context.ShowHint(Strings.MirroringOff, Strings.MutedDevices(snapshot.Count), System.Windows.Forms.ToolTipIcon.Info);
            return;
        }

        Dictionary<string, DeviceSetting>? saved = settings.HotkeySnapshot;
        if (saved == null || saved.Count == 0)
        {
            SetStatus(Strings.NoRememberedState, true);
            return;
        }

        int restored = 0;
        int skipped = 0;
        suppress = true;
        foreach ((string deviceId, DeviceSetting entry) in saved)
        {
            DeviceViewModel? device = Devices.FirstOrDefault(d =>
                d.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) && !d.IsSource);
            if (device == null)
            {
                // Gone meanwhile, or now the source itself.
                skipped++;
                continue;
            }

            DeviceSetting target = settings.For(deviceId);
            target.Enabled = true;
            target.Volume = entry.Volume;
            foreach ((string appKey, AppMixSetting app) in entry.Apps)
            {
                target.Apps[appKey] = new AppMixSetting { Enabled = app.Enabled, Volume = app.Volume };
            }
            device.Volume = entry.Volume;
            device.IsEnabled = true;
            restored++;
        }
        suppress = false;

        settings.HotkeySnapshot = null;
        Save();
        // Rebuild the app rows so they show the restored mix.
        foreach (DeviceViewModel device in Devices)
        {
            foreach (AppViewModel app in device.Apps) app.PropertyChanged -= OnAppChanged;
            device.Apps.Clear();
        }
        RefreshAppLists();
        SyncTargets();
        RaiseToggle();
        context.ShowHint(Strings.MirroringOn, skipped == 0
            ? Strings.RestoredDevices(restored)
            : Strings.RestoredDevicesPartly(restored, skipped), System.Windows.Forms.ToolTipIcon.Info);
    }

    // ══ Timing ══════════════════════════════════════════════════════════════

    private void ScheduleRefresh()
    {
        needsDeviceRefresh = true;
        debounceTimer.Stop();
        debounceTimer.Start();
    }

    private void OnDebounceTick()
    {
        debounceTimer.Stop();
        if (needsDeviceRefresh)
        {
            needsDeviceRefresh = false;
            RefreshDevices();
            RefreshAppLists();
        }
        SyncTargets();
    }

    private void OnStatusTick()
    {
        // New apps must be captured even while the flyout is closed; the status text only
        // matters while it is open.
        if (++statusTicks % AppListEveryTicks == 0 && RefreshAppLists())
        {
            SyncTargets();
        }
        else if (context.IsFlyoutVisible())
        {
            RefreshStatus();
        }
    }

    private void Post(Action action)
    {
        if (disposed) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!disposed) action();
        });
    }

    private void Save()
    {
        if (!context.IsPreview) context.Settings.Save();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        statusTimer.Stop();
        debounceTimer.Stop();
        engine.Dispose();
    }

    /// <summary>The hotkey action: everything off, or back on.</summary>
    private sealed class ToggleMirroringFeature(AudioMirrorModule module) : IFeature
    {
        public string Id => "audio-mirror-toggle";
        public string Name => "Toggle mirroring";
        public string Description => "Turns all mirroring off and restores it on the next press.";
        public string Glyph => "";
        public Hotkey? DefaultHotkey => null;
        public string? Status => null;

        public Task ExecuteAsync()
        {
            module.ToggleEverything();
            return Task.CompletedTask;
        }
    }
}
