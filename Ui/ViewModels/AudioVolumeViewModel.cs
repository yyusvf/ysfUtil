using YsfUtil.Audio;

namespace YsfUtil.Ui.ViewModels;

internal sealed class AudioVolumeViewModel : ViewModelBase
{
    private readonly AudioVolumeController volume;
    private readonly Action save;
    private bool updating;
    public AudioVolumeViewModel(AudioVolumeController volume, Action save)
    {
        this.volume = volume;
        this.save = save;
        volume.Changed += Update;
        Refresh();
    }

    public bool IsEnabled
    {
        get => volume.Settings.Enabled;
        set { volume.SetEnabled(value); save(); }
    }
    public bool HasOutput => volume.State.Output != null;
    public bool HasInput => volume.State.Input != null;
    public string OutputName => Name(volume.State.Output, "No output connected");
    public string InputName => Name(volume.State.Input, "No microphone connected");
    public double OutputVolume
    {
        get => volume.State.Output?.Percent ?? 0;
        set
        {
            if (!updating && Math.Abs(value - OutputVolume) > 0.01) volume.SetOutputVolume(value);
        }
    }
    public double InputVolume
    {
        get => volume.State.Input?.Percent ?? 0;
        set
        {
            if (!updating && Math.Abs(value - InputVolume) > 0.01) { volume.SetInputVolume(value); save(); }
        }
    }
    public string OutputText => $"{OutputVolume:0}%";
    public string InputText => $"{InputVolume:0}%";
    public bool LockMicrophone
    {
        get => volume.Settings.LockMicrophone;
        set { volume.SetMicrophoneLock(value); save(); }
    }
    public string LockLabel => $"Keep microphone at {volume.Settings.MicrophoneTarget}%";
    public string? Error => volume.Error;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public void Refresh() => volume.Refresh();
    private void Update()
    {
        updating = true;
        try
        {
            foreach (string property in new[] { nameof(IsEnabled), nameof(HasOutput), nameof(HasInput),
                nameof(OutputName), nameof(InputName), nameof(OutputVolume), nameof(InputVolume),
                nameof(OutputText), nameof(InputText), nameof(LockMicrophone), nameof(LockLabel), nameof(Error), nameof(HasError) }) Raise(property);
        }
        finally { updating = false; }
    }
    private static string Name(AudioVolumeEndpoint? device, string missing) => device == null ? missing
        : device.Name + (device.Muted ? " · Muted" : "");
}
