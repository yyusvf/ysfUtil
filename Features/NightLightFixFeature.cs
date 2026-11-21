using System.IO;
using Microsoft.Win32;
using YsfUtil.Hotkeys;

namespace YsfUtil.Features;

/// <summary>
/// Repairs Windows night light when it gets out of step with its switch: still tinted after
/// switching it off, or no tint while it is on.
///
/// Windows does not report the tint it actually applies, so this cannot tell whether it is stuck.
/// It forces Windows to apply the current state again, the way it reacts when the switch or the
/// strength slider is moved:
/// - Off: on at strength 0 (no tint), off again, then the old strength back. Even if switching
///   off fails to apply again, what is left on screen is no tint.
/// - On: off, the strength written again, on again.
/// The switch and strength end as they were.
/// </summary>
internal sealed class NightLightFixFeature : IFeature
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\";
    private const string StateKey = Store + @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
    private const string SettingsKey = Store + @"default$windows.data.bluelightreduction.settings\windows.data.bluelightreduction.settings";

    /// <summary>Time Windows needs to pick up one change before the next.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    private int running;

    public string Id => "night-light-fix";
    public string Name => "Fix night light";
    public string Description => "Applies night light again when it stays tinted after switching it off, or shows no tint while on.";
    public string Glyph => ""; // QuietHours (moon) in Segoe Fluent Icons
    public Hotkey? DefaultHotkey => null;

    public string? Status
    {
        get
        {
            if (Volatile.Read(ref running) != 0) return "Applying night light again…";
            try
            {
                NightLightData? state = Read(StateKey);
                if (state == null) return "Night light was never used on this PC";
                int strength = NightLightData.Strength(Read(SettingsKey)?.Kelvin ?? NightLightData.DefaultKelvin);
                return state.IsOn ? $"On, strength {strength}%" : $"Off, strength {strength}%";
            }
            catch
            {
                return "Night light state unknown";
            }
        }
    }

    public async Task ExecuteAsync()
    {
        if (Interlocked.Exchange(ref running, 1) != 0) return;
        try
        {
            await Task.Run(async () =>
            {
                NightLightData state = Read(StateKey)
                    ?? throw new InvalidOperationException("Night light was never used on this PC. Switch it on once in Windows Settings.");
                NightLightData? settings = Read(SettingsKey);

                if (state.IsOn)
                {
                    state = WriteState(state, on: false);
                    await Task.Delay(Step);
                    if (settings != null)
                    {
                        Write(SettingsKey, settings.ToBytes(DateTimeOffset.Now));
                        await Task.Delay(Step);
                    }
                    WriteState(state, on: true);
                }
                else
                {
                    int kelvin = settings?.Kelvin ?? NightLightData.DefaultKelvin;
                    // Without saved settings there is no strength to set to 0, so a plain on and off.
                    if (settings != null)
                    {
                        settings = WriteSettings(settings, NightLightData.NeutralKelvin);
                        await Task.Delay(Step);
                    }
                    try
                    {
                        state = WriteState(state, on: true);
                        await Task.Delay(Step);
                        WriteState(state, on: false);
                        await Task.Delay(Step);
                    }
                    finally
                    {
                        if (settings != null) WriteSettings(settings, kelvin);
                    }
                }
            });
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(ex.Message + " Nothing was changed.", ex);
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    private static NightLightData WriteState(NightLightData state, bool on)
    {
        NightLightData next = state.WithOn(on, DateTimeOffset.UtcNow);
        byte[] bytes = next.ToBytes(DateTimeOffset.Now);
        Write(StateKey, bytes);
        return NightLightData.Parse(bytes);
    }

    private static NightLightData WriteSettings(NightLightData settings, int kelvin)
    {
        byte[] bytes = settings.WithKelvin(kelvin).ToBytes(DateTimeOffset.Now);
        Write(SettingsKey, bytes);
        return NightLightData.Parse(bytes);
    }

    private static NightLightData? Read(string path)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue("Data") is byte[] data ? NightLightData.Parse(data) : null;
    }

    private static void Write(string path, byte[] data)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(path, writable: true)
            ?? throw new InvalidOperationException("Windows night light settings are missing.");
        key.SetValue("Data", data, RegistryValueKind.Binary);
    }
}
