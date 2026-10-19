namespace YsfUtil.Audio;

internal sealed class AudioVolumeSettings
{
    public bool Enabled { get; set; } = true;
    public bool LockMicrophone { get; set; }
    public int MicrophoneTarget { get; set; } = 100;
}

internal sealed record AudioVolumeEndpoint(string Id, string Name, double Percent, bool Muted);
internal sealed record AudioVolumeState(AudioVolumeEndpoint? Output, AudioVolumeEndpoint? Input);

internal interface IAudioVolumeBackend
{
    AudioVolumeState Read();
    void SetOutputVolume(string id, double percent);
    void SetInputVolume(string id, double percent);
}

/// <summary>Controls endpoint levels and restores an opted-in microphone target.</summary>
internal sealed class AudioVolumeController(IAudioVolumeBackend backend, AudioVolumeSettings settings)
{
    private string? enforcedDevice;
    private double effectiveTarget;
    private int requestedTarget = -1;
    private bool refreshFailed;

    public AudioVolumeSettings Settings { get; } = settings;
    public AudioVolumeState State { get; private set; } = new(null, null);
    public string? Error { get; private set; }
    public event Action? Changed;

    public void Refresh()
    {
        try
        {
            State = backend.Read();
            EnforceMicrophone();
            if (refreshFailed) Error = null;
            refreshFailed = false;
        }
        catch (Exception ex)
        {
            State = new(null, null);
            refreshFailed = true;
            Error = "Volume controls unavailable: " + ex.Message;
        }
        Changed?.Invoke();
    }

    public void SetOutputVolume(double percent) => Run(() =>
    {
        Validate(percent);
        if (!Settings.Enabled || State.Output == null) throw new InvalidOperationException("No default output is available.");
        backend.SetOutputVolume(State.Output.Id, percent);
        State = backend.Read();
    });

    public void SetInputVolume(double percent) => Run(() =>
    {
        Validate(percent);
        if (!Settings.Enabled || State.Input == null) throw new InvalidOperationException("No default microphone is available.");
        backend.SetInputVolume(State.Input.Id, percent);
        Settings.MicrophoneTarget = (int)Math.Round(percent);
        requestedTarget = -1;
        State = backend.Read();
        EnforceMicrophone();
    });

    public void SetMicrophoneLock(bool enabled)
    {
        Settings.LockMicrophone = enabled;
        requestedTarget = -1;
        Refresh();
    }

    public void SetEnabled(bool enabled)
    {
        Settings.Enabled = enabled;
        if (!enabled) Settings.LockMicrophone = false;
        Refresh();
    }

    private void EnforceMicrophone()
    {
        if (!Settings.Enabled || !Settings.LockMicrophone || State.Input is not { } input) return;
        int target = Math.Clamp(Settings.MicrophoneTarget, 0, 100);
        if (enforcedDevice == input.Id && requestedTarget == target && Math.Abs(input.Percent - effectiveTarget) < 0.1) return;
        backend.SetInputVolume(input.Id, target);
        State = backend.Read();
        if (State.Input?.Id != input.Id) { requestedTarget = -1; return; }
        enforcedDevice = input.Id;
        requestedTarget = target;
        // Drivers may quantize their level (e.g. 73% -> 72.7%). Remember the actual level so
        // our own volume notification cannot trigger an endless write/notification loop.
        effectiveTarget = State.Input.Percent;
    }

    private void Run(Action action)
    {
        Error = null;
        try { action(); }
        catch (Exception ex) { Error = ex.Message; }
        Changed?.Invoke();
    }

    private static void Validate(double percent)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "Choose a volume between 0 and 100%.");
    }
}
