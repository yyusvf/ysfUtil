namespace YsfUtil.Modules.AudioMirror;

/// <summary>
/// The Audio Mirror texts, in English like the rest of ysfUtil. The standalone app had thirteen
/// languages; once ysfUtil gets a language system these are the strings to translate.
/// </summary>
internal static class Strings
{
    // ── Engine errors ────────────────────────────────────────────────────────
    public const string AccessDenied = "Access to the device was denied.";
    public const string AppNotRunning = "Application is not running.";
    public const string AppSilent = "Application is not playing any sound right now.";
    public const string AudioServiceDown = "The Windows audio service is not running.";
    public static string CaptureFailed(string message) => "Sound cannot be captured: " + message;
    public const string DeviceExclusive = "Device is used exclusively by another application.";
    public const string DeviceGone = "Device is no longer available (disconnected or disabled).";
    public const string DeviceInUse = "Device is already in use by this application.";
    public const string FormatUnsupported = "This device does not support the audio format.";
    public const string NoSourceDevice = "No source device available.";
    public const string PlaybackStopped = "Playback was stopped by the system.";
    public static string SourceCaptureFailed(string message) => "Could not start capturing the source device: " + message;
    public const string SourceEnded = "The audio source was stopped.";
    public const string UnknownDevice = "Unknown device";
    public const string UnknownError = "Unknown error.";

    // ── Status ───────────────────────────────────────────────────────────────
    public const string DeviceOrApp = "device or application";
    public const string LastChosenUnavailable = "Last selected device (unavailable)";
    public const string NoAppSelected = "No application selected";
    public const string NoDeviceAvailable = "none available";
    public const string NothingTicked = "No output device selected";
    public const string RetryRunning = " – retrying";
    public static string RunningApps(int count, double ms) => $"{count} application(s), about {ms:0} ms";
    public static string RunningWholeSound(double ms) => $"Full device sound, about {ms:0} ms";
    public const string SourceShort = "Source";
    public const string SourceUnavailable = "Source device unavailable, mirroring is paused";
    public static string MirroringOnDevices(int running) => $"Mirroring to {running} device(s)";
    public static string StillWaiting(int failed) => $", {failed} still waiting";
    public static string WaitingFor(string reason, int selected) => $"Waiting for {reason} ({selected} device(s) selected)";
    public static string WindowsDefaultDevice(string current) => $"Windows default ({current})";

    // ── Hotkey ───────────────────────────────────────────────────────────────
    public const string MirroringOff = "Mirroring off";
    public const string MirroringOn = "Mirroring on";
    public static string MutedDevices(int count) => $"{count} device(s) muted. Press again to restore them.";
    public const string NoRememberedState = "Nothing remembered yet. Select a device first.";
    public static string RestoredDevices(int restored) => $"{restored} device(s) restored.";
    public static string RestoredDevicesPartly(int restored, int skipped) =>
        $"{restored} device(s) restored, {skipped} no longer available.";
}
