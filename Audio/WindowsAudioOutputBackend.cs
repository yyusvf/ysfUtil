using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace YsfUtil.Audio;

internal sealed class WindowsAudioOutputBackend : IAudioOutputBackend, IMMNotificationClient, IDisposable
{
    private MMDeviceEnumerator? enumerator;
    private bool registered;
    private bool disposed;
    public event Action? DevicesChanged;

    private MMDeviceEnumerator Enumerator
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            enumerator ??= new MMDeviceEnumerator();
            if (!registered)
            {
                try
                {
                    Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(this));
                    registered = true;
                }
                catch (COMException) { /* Manual refresh remains available if Windows audio is restarting. */ }
            }
            return enumerator;
        }
    }

    public AudioOutputSnapshot Read()
    {
        var devices = new List<AudioOutputDevice>();
        foreach (var device in Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged))
        {
            using (device)
            {
                try
                {
                    string glyph = "\uE995";
                    if (device.Properties.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor))
                    {
                        int form = Convert.ToInt32(device.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value);
                        glyph = form switch { 3 or 5 => "\uE7F6", 9 => "\uE7F4", 7 or 8 => "\uE964", _ => "\uE995" };
                    }
                    devices.Add(new(device.ID, device.FriendlyName, device.State == DeviceState.Active, glyph));
                }
                catch (COMException) { /* Endpoint disappeared during enumeration. */ }
            }
        }
        return new(devices.OrderByDescending(d => d.Connected)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), DefaultId(Role.Multimedia));
    }

    public string? DefaultId(Role role)
    {
        try
        {
            using var device = Enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            return device.ID;
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070490)) { return null; }
    }

    public void SetDefault(string id, bool communications)
    {
        using var device = Enumerator.GetDevice(id);
        if (device.State != DeviceState.Active)
            throw new InvalidOperationException("The selected output is not connected.");

        Role[] roles = communications ? [Role.Console, Role.Multimedia, Role.Communications]
            : [Role.Console, Role.Multimedia];
        var previous = roles.ToDictionary(r => r, DefaultId);
        object policy = Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), true)!)!;
        try
        {
            var config = (IPolicyConfig)policy;
            try
            {
                foreach (Role role in roles)
                    Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(id, role));
            }
            catch
            {
                // Best-effort rollback if only some of the roles could be changed.
                foreach (var entry in previous)
                    if (entry.Value != null) config.SetDefaultEndpoint(entry.Value, entry.Key);
                throw;
            }
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    public void OnDeviceStateChanged(string id, DeviceState state) => Notify();
    public void OnDeviceAdded(string id) => Notify();
    public void OnDeviceRemoved(string id) => Notify();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) => Notify();
    public void OnPropertyValueChanged(string id, PropertyKey key) => Notify();
    private void Notify() { if (!disposed) DevicesChanged?.Invoke(); }
    public void Dispose()
    {
        disposed = true;
        if (enumerator == null) return;
        try { if (registered) enumerator.UnregisterEndpointNotificationCallback(this); }
        finally { enumerator.Dispose(); }
    }

    // Windows exposes default-device switching through this system COM interface. Keep the
    // native method order; only SetDefaultEndpoint is invoked. No SoundSwitch code is used.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int defaults, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int defaults, out long period, out long minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
