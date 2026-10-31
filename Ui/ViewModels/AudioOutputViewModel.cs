using System.Collections.ObjectModel;
using YsfUtil.Audio;

namespace YsfUtil.Ui.ViewModels;

internal sealed class AudioOutputDeviceViewModel : ViewModelBase
{
    private readonly AudioOutputPreference preference;
    private readonly Action save;
    public AudioOutputDeviceViewModel(AudioOutputPreference preference, Action save)
    {
        this.preference = preference;
        this.save = save;
    }
    public string Id => preference.Id;
    public string Name => preference.Name;
    public string Glyph { get; private set; } = "\uE995";
    public bool Connected { get; private set; }
    public bool IsCurrent { get; private set; }
    public string Status => !Connected ? "Not connected" : IsCurrent ? "Current output" : Included ? "In hotkey cycle" : "Available";
    public bool Included
    {
        get => preference.Included;
        set
        {
            if (preference.Included == value) return;
            preference.Included = value;
            Raise(); Raise(nameof(Status));
            save();
        }
    }
    public void Update(AudioOutputDevice? device, string? current)
    {
        Connected = device?.Connected == true;
        Glyph = device?.Glyph ?? "\uE995";
        IsCurrent = Connected && string.Equals(Id, current, StringComparison.OrdinalIgnoreCase);
        Raise(nameof(Name)); Raise(nameof(Glyph)); Raise(nameof(Connected));
        Raise(nameof(IsCurrent)); Raise(nameof(Included)); Raise(nameof(Status));
    }
    public override string ToString() => Name;
}

internal sealed class AudioOutputViewModel : ViewModelBase
{
    private readonly AudioOutputSwitcher audio;
    private readonly Action save;
    private bool updating;
    private AudioOutputDeviceViewModel? selectedOutput;
    private string? refreshError;

    public AudioOutputViewModel(AudioOutputSwitcher audio, FeatureViewModel feature, Action save)
    {
        this.audio = audio;
        this.save = save;
        Feature = feature;
        NextCommand = new RelayCommand(() => { if (Feature.IsEnabled) Feature.RunCommand.Execute(null); });
        RefreshCommand = new RelayCommand(Refresh);
        audio.Changed += Update;
        Refresh();
    }

    public FeatureViewModel Feature { get; }
    public ObservableCollection<AudioOutputDeviceViewModel> Devices { get; } = [];
    public ObservableCollection<AudioOutputDeviceViewModel> AvailableOutputs { get; } = [];
    public bool HasOutputs => AvailableOutputs.Count > 0;
    public bool CanSwitch => Feature.IsEnabled && HasOutputs && !audio.IsSwitching;
    public string CurrentName => audio.Current?.Name ?? "No output device connected";
    public string CurrentGlyph => audio.Current?.Glyph ?? "\uE995";
    public string CycleSummary => audio.IsSwitching ? "Switching output…"
        : $"{Devices.Count(d => d.Included && d.Connected)} outputs in hotkey cycle";
    public string? Error => refreshError ?? audio.Error;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public RelayCommand NextCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public bool SwitchCommunications
    {
        get => audio.Settings.SwitchCommunications;
        set { audio.Settings.SwitchCommunications = value; Raise(); save(); }
    }
    public bool ShowBanner
    {
        get => audio.Settings.ShowBanner;
        set { audio.Settings.ShowBanner = value; Raise(); save(); }
    }
    public AudioOutputDeviceViewModel? SelectedOutput
    {
        get => selectedOutput;
        set
        {
            if (!Set(ref selectedOutput, value) || updating || value == null || !Feature.IsEnabled) return;
            if (string.Equals(value.Id, audio.Snapshot.DefaultId, StringComparison.OrdinalIgnoreCase)) return;
            _ = SelectOutputAsync(value.Id);
        }
    }

    private async Task SelectOutputAsync(string id)
    {
        try { await audio.SelectAsync(id); }
        catch { Update(); } // Observe failures; the switcher also exposes them inline and in the overlay.
    }

    public void Refresh()
    {
        try { refreshError = null; audio.Refresh(); }
        catch (Exception ex) { refreshError = "Audio devices unavailable: " + ex.Message; Update(); }
    }

    private void Update()
    {
        updating = true;
        try
        {
            // Only what Windows lists right now; remembered outputs that are gone stay in the
            // settings for the cycle but don't clutter the list.
            var listed = audio.Listed;
            foreach (var row in Devices.Where(r => !listed.Any(d => string.Equals(d.Id, r.Id, StringComparison.OrdinalIgnoreCase))).ToList())
                Devices.Remove(row);
            for (int index = 0; index < listed.Count; index++)
            {
                var device = listed[index];
                var preference = audio.Settings.Devices.FirstOrDefault(p => string.Equals(p.Id, device.Id, StringComparison.OrdinalIgnoreCase));
                if (preference == null) continue;
                var row = Devices.FirstOrDefault(d => string.Equals(d.Id, device.Id, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                {
                    row = new AudioOutputDeviceViewModel(preference, () =>
                    {
                        audio.Settings.SelectionConfigured = true;
                        save(); Raise(nameof(CycleSummary));
                    });
                    Devices.Insert(Math.Min(index, Devices.Count), row);
                }
                else if (Devices.IndexOf(row) is int at && at != index && index < Devices.Count)
                {
                    Devices.Move(at, index);
                }
                row.Update(device, audio.Snapshot.DefaultId);
            }
            // Keep collection items stable so device notifications do not steal keyboard focus.
            var connected = Devices.Where(d => d.Connected).ToList();
            foreach (var row in AvailableOutputs.Where(d => !connected.Contains(d)).ToList()) AvailableOutputs.Remove(row);
            foreach (var row in connected.Where(d => !AvailableOutputs.Contains(d))) AvailableOutputs.Add(row);
            SelectedOutput = AvailableOutputs.FirstOrDefault(d => string.Equals(d.Id, audio.Snapshot.DefaultId, StringComparison.OrdinalIgnoreCase));
        }
        finally { updating = false; }
        Feature.Refresh();
        Raise(nameof(CurrentName)); Raise(nameof(CurrentGlyph)); Raise(nameof(HasOutputs));
        Raise(nameof(CanSwitch));
        Raise(nameof(CycleSummary)); Raise(nameof(Error)); Raise(nameof(HasError));
    }
}
