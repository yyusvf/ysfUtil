using System.IO;
using System.Text.Json;
using YsfUtil.Hotkeys;
using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Modules.AudioMirror;

/// <summary>On/off and volume of one application on one output device.</summary>
internal sealed class AppMixSetting
{
    /// <summary>Newly appearing applications are mirrored until switched off.</summary>
    public bool Enabled { get; set; } = true;

    public float Volume { get; set; } = 1f;
}

internal sealed class DeviceSetting
{
    public bool Enabled { get; set; }

    /// <summary>Master volume of the device; multiplies with every application's volume.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Keyed by executable name, which - unlike the process id - survives restarts.</summary>
    public Dictionary<string, AppMixSetting> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Device selection, volumes and app mix, saved within ysfUtil's settings. Keyed by the stable
/// WASAPI endpoint id, never by display name.
/// </summary>
internal sealed class AudioMirrorSettings
{
    public Dictionary<string, DeviceSetting> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int BufferMs { get; set; } = 30;

    /// <summary>A fixed source device; null follows the Windows default device.</summary>
    public string? SourceDeviceId { get; set; }

    /// <summary>
    /// What was on when the hotkey turned mirroring off, restored by the next press. Survives a
    /// restart on purpose.
    /// </summary>
    public Dictionary<string, DeviceSetting>? HotkeySnapshot { get; set; }

    public DeviceSetting For(string deviceId)
    {
        if (!Devices.TryGetValue(deviceId, out DeviceSetting? setting))
        {
            setting = new DeviceSetting();
            Devices[deviceId] = setting;
        }
        // Dictionaries from the file come back without the case-insensitive comparer.
        if (setting.Apps?.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            setting.Apps = new(setting.Apps ?? [], StringComparer.OrdinalIgnoreCase);
        }
        return setting;
    }

    /// <summary>An app's stored state without creating an entry. Unknown apps are mirrored.</summary>
    public (bool Enabled, float Volume) Lookup(string deviceId, string appKey) =>
        Devices.TryGetValue(deviceId, out DeviceSetting? device)
        && device.Apps?.TryGetValue(appKey, out AppMixSetting? app) == true
            ? (app.Enabled, app.Volume)
            : (true, 1f);

    public AppMixSetting For(string deviceId, string appKey)
    {
        DeviceSetting device = For(deviceId);
        if (!device.Apps.TryGetValue(appKey, out AppMixSetting? app))
        {
            app = new AppMixSetting();
            device.Apps[appKey] = app;
        }
        return app;
    }

    /// <summary>
    /// The standalone Audio Mirror's settings from %APPDATA%\AudioMirror\settings.json, so moving to
    /// ysfUtil keeps devices, volumes and the app mix. Also returns its toggle hotkey, if any.
    /// </summary>
    public static (AudioMirrorSettings Settings, Hotkey? Hotkey) ImportStandalone()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioMirror", "settings.json");
        try
        {
            if (File.Exists(path))
            {
                var old = JsonSerializer.Deserialize<StandaloneSettings>(File.ReadAllText(path));
                if (old != null)
                {
                    var settings = new AudioMirrorSettings
                    {
                        Devices = new(old.Devices ?? [], StringComparer.OrdinalIgnoreCase),
                        BufferMs = Math.Clamp(old.BufferMs, 10, 250),
                        SourceDeviceId = old.SourceDeviceId,
                        HotkeySnapshot = old.HotkeySnapshot,
                    };
                    return (settings, old.HotkeyToggleAllEnabled ? ToHotkey((Keys)old.HotkeyToggleAll) : null);
                }
            }
        }
        catch
        {
            // Unreadable - start fresh rather than not at all.
        }
        return (new AudioMirrorSettings(), null);
    }

    /// <summary>The standalone app stored a WinForms Keys value: key code plus modifier bits.</summary>
    private static Hotkey? ToHotkey(Keys value)
    {
        Keys key = value & Keys.KeyCode;
        if (!Hotkey.IsUsableKey(key)) return null;
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if (value.HasFlag(Keys.Control)) modifiers |= HotkeyModifiers.Control;
        if (value.HasFlag(Keys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (value.HasFlag(Keys.Shift)) modifiers |= HotkeyModifiers.Shift;
        return new Hotkey(modifiers, key);
    }

    /// <summary>The fields of the standalone app's file that are worth keeping.</summary>
    private sealed class StandaloneSettings
    {
        public Dictionary<string, DeviceSetting>? Devices { get; set; }
        public int BufferMs { get; set; } = 30;
        public string? SourceDeviceId { get; set; }
        public int HotkeyToggleAll { get; set; }
        public bool HotkeyToggleAllEnabled { get; set; } = true;
        public Dictionary<string, DeviceSetting>? HotkeySnapshot { get; set; }
    }
}
