using YsfUtil.Audio;

internal static class AudioVolumeChecks
{
    public static void Run()
    {
        var backend = new FakeVolumeBackend();
        var settings = new AudioVolumeSettings();
        var controller = new AudioVolumeController(backend, settings);
        controller.Refresh();
        Check(backend.InputWrites == 0 && !settings.LockMicrophone, "Microphone forcing must be opt-in.");
        controller.SetOutputVolume(73);
        Check(backend.Output == 73 && backend.Input == 75, "Output slider changed the input.");
        controller.SetInputVolume(66);
        Check(settings.MicrophoneTarget == 66 && backend.Input == 66, "Microphone slider did not update its target.");
        controller.SetMicrophoneLock(true);
        int writes = backend.InputWrites;
        for (int i = 0; i < 20; i++) controller.Refresh();
        Check(backend.InputWrites == writes, "An unchanged microphone level caused repeated writes.");
        backend.Input = 20;
        controller.Refresh();
        Check(backend.Input == 66, "An external app's level reduction was not restored.");
        backend.Muted = true;
        backend.Input = 12;
        controller.Refresh();
        Check(backend.Input == 66 && controller.State.Input!.Muted, "Forcing a level must preserve mute.");

        backend.Quantize = true;
        controller.SetInputVolume(73);
        Check(backend.Input == 70, "Fake device quantization did not apply.");
        writes = backend.InputWrites;
        for (int i = 0; i < 20; i++) controller.Refresh();
        Check(backend.InputWrites == writes, "Quantized hardware caused a notification/write loop.");
        backend.Input = 10;
        controller.Refresh();
        Check(backend.Input == 70, "Quantized hardware was not restored to the chosen target.");
        backend.HasInput = false;
        controller.Refresh();
        Check(controller.State.Input == null && settings.LockMicrophone, "Disconnecting lost the microphone lock preference.");
        backend.HasInput = true;
        backend.InputId = "second-mic";
        backend.Input = 20;
        controller.Refresh();
        Check(backend.Input == 70 && settings.MicrophoneTarget == 73, "The lock did not follow the new default microphone.");

        controller.SetMicrophoneLock(false);
        backend.Input = 10;
        controller.Refresh();
        Check(backend.Input == 10, "An unlocked microphone was still forced.");
        controller.SetMicrophoneLock(true);
        controller.SetEnabled(false);
        backend.Input = 10;
        controller.Refresh();
        Check(backend.Input == 10 && !settings.LockMicrophone, "Disabling volume controls must stop forcing.");
        controller.SetEnabled(true);
        foreach (double invalid in new[] { -1d, 101d, double.NaN, double.PositiveInfinity })
        {
            writes = backend.InputWrites;
            controller.SetInputVolume(invalid);
            Check(backend.InputWrites == writes && controller.Error != null, "Invalid volume reached the backend.");
        }
        backend.Fail = true;
        controller.Refresh();
        Check(controller.State.Input == null && controller.Error != null, "Unavailable audio service was not reported.");
        backend.Fail = false;
        controller.Refresh();
        Check(controller.State.Input != null && controller.Error == null, "Volume controls did not recover.");
        Console.WriteLine("PASS: output/input sliders, opt-in microphone lock, external reduction, mute preservation, quantization, default-device changes, disconnect/reconnect, disable, validation and recovery.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class FakeVolumeBackend : IAudioVolumeBackend
    {
        public double Output = 42, Input = 75;
        public string InputId = "mic";
        public bool Muted, Quantize, Fail;
        public bool HasInput = true;
        public int InputWrites;
        public AudioVolumeState Read()
        {
            if (Fail) throw new InvalidOperationException("Audio service stopped.");
            return new(new("out", "Speakers", Output, false), HasInput ? new(InputId, "Microphone", Input, Muted) : null);
        }
        public void SetOutputVolume(string id, double percent) => Output = percent;
        public void SetInputVolume(string id, double percent)
        {
            InputWrites++;
            Input = Quantize ? Math.Round(percent / 10) * 10 : percent;
        }
    }
}
