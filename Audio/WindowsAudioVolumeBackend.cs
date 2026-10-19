using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace YsfUtil.Audio;

/// <summary>Volume metadata only: no recording, playback stream or microphone capture.</summary>
internal sealed class WindowsAudioVolumeBackend : IAudioVolumeBackend, IDisposable
{
    private MMDeviceEnumerator? enumerator;
    private MMDevice? output;
    private MMDevice? input;
    private bool disposed;
    public event Action? VolumeChanged;
    private MMDeviceEnumerator Enumerator => enumerator ??= new MMDeviceEnumerator();

    public AudioVolumeState Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(ReadEndpoint(ref output, DataFlow.Render), ReadEndpoint(ref input, DataFlow.Capture));
    }

    private AudioVolumeEndpoint? ReadEndpoint(ref MMDevice? cached, DataFlow flow)
    {
        MMDevice? current;
        try { current = Enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070490))
        {
            Release(ref cached);
            return null;
        }
        if (cached?.ID != current.ID)
        {
            Release(ref cached);
            cached = current;
            cached.AudioEndpointVolume.OnVolumeNotification += OnVolumeChanged;
        }
        else current.Dispose();
        var volume = cached.AudioEndpointVolume;
        return new(cached.ID, cached.FriendlyName, volume.MasterVolumeLevelScalar * 100d, volume.Mute);
    }

    public void SetOutputVolume(string id, double percent) => Set(ref output, DataFlow.Render, id, percent);
    public void SetInputVolume(string id, double percent) => Set(ref input, DataFlow.Capture, id, percent);
    private void Set(ref MMDevice? device, DataFlow flow, string id, double percent)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        ReadEndpoint(ref device, flow);
        if (device == null || !string.Equals(device.ID, id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The default audio device changed. Try adjusting its volume again.");
        // Setting the level does not unmute the endpoint or change its microphone privacy state.
        device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(percent / 100d);
    }

    private void OnVolumeChanged(AudioVolumeNotificationData data) { if (!disposed) VolumeChanged?.Invoke(); }
    private void Release(ref MMDevice? device)
    {
        if (device == null) return;
        try
        {
            device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeChanged;
            device.Dispose();
        }
        catch (COMException) { /* An unplugged endpoint may already have been invalidated. */ }
        finally { device = null; }
    }
    public void Dispose()
    {
        disposed = true;
        Release(ref output);
        Release(ref input);
        enumerator?.Dispose();
    }
}
