namespace YsfUtil.Audio;

internal sealed record AudioOutputDevice(string Id, string Name, bool Connected, string Glyph = "\uE995");
internal sealed record AudioOutputSnapshot(IReadOnlyList<AudioOutputDevice> Devices, string? DefaultId);

internal interface IAudioOutputBackend
{
    AudioOutputSnapshot Read();
    void SetDefault(string id, bool communications);
}

/// <summary>Device selection and cycling, independent of Windows and the UI.</summary>
internal sealed class AudioOutputSwitcher(IAudioOutputBackend backend, AudioOutputSettings settings,
    Func<TimeSpan, Task>? delay = null)
{
    public AudioOutputSettings Settings { get; } = settings;
    public AudioOutputSnapshot Snapshot { get; private set; } = new([], null);
    public AudioOutputDevice? Current => Snapshot.Devices.FirstOrDefault(d => SameId(d.Id, Snapshot.DefaultId));
    public string? Error { get; private set; }
    public bool IsSwitching { get; private set; }
    public event Action? Changed;
    public event Action<AudioOutputDevice>? Switched;
    public event Action<string>? Failed;

    public void Refresh()
    {
        Snapshot = backend.Read();
        bool Present(AudioOutputPreference saved) => Snapshot.Devices.Any(d => SameId(d.Id, saved.Id));
        foreach (var device in Snapshot.Devices)
        {
            var saved = Settings.Devices.FirstOrDefault(d => SameId(d.Id, device.Id))
                // Windows sometimes gives an output a new endpoint id (driver update, another port,
                // "2- " in the name). Carry the remembered entry over instead of listing it twice.
                ?? Settings.Devices.FirstOrDefault(d => SameDevice(d.Name, device.Name) && !Present(d));
            if (saved == null)
            {
                Settings.Devices.Add(new AudioOutputPreference
                {
                    Id = device.Id, Name = device.Name,
                    Included = !Settings.SelectionConfigured && device.Connected,
                });
            }
            else
            {
                saved.Id = device.Id;
                saved.Name = device.Name;
            }
        }
        // Gone from Windows: forget it unless it is in the cycle and could come back.
        Settings.Devices.RemoveAll(d => !Present(d)
            && (!d.Included || Snapshot.Devices.Any(device => SameDevice(device.Name, d.Name))));
        Changed?.Invoke();
    }

    /// <summary>
    /// The outputs to list, like the Windows sound settings: what Windows knows right now, connected
    /// first. An unplugged endpoint is left out when a connected one has the same name.
    /// </summary>
    public IReadOnlyList<AudioOutputDevice> Listed
    {
        get
        {
            var connected = Snapshot.Devices.Where(d => d.Connected).ToList();
            var unplugged = Snapshot.Devices
                .Where(d => !d.Connected && !connected.Any(c => SameDevice(c.Name, d.Name)))
                .GroupBy(d => Normalize(d.Name), StringComparer.CurrentCultureIgnoreCase)
                .Select(g => g.First());
            return connected.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Concat(unplugged.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                .ToList();
        }
    }

    /// <summary>"Kopfhörer (2- JadeAudio)" and "Kopfhörer (JadeAudio)", "4 - TV" and "TV" are one device.</summary>
    internal static bool SameDevice(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.CurrentCultureIgnoreCase);

    private static string Normalize(string name) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"^\d+\s*-\s*", ""), @"\(\d+-\s*", "(");

    public Task CycleAsync()
    {
        return RunAsync(async () =>
        {
            Refresh(); // Never cycle to an endpoint that has just been unplugged.
            var candidates = Settings.Devices
                .Where(p => p.Included && Snapshot.Devices.Any(d => d.Connected && SameId(d.Id, p.Id)))
                .Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException("Choose connected outputs in Settings → Audio output.");
            int index = candidates.FindIndex(id => SameId(id, Snapshot.DefaultId));
            await SetDefaultAsync(candidates[(index + 1) % candidates.Count]);
        });
    }

    public Task SelectAsync(string id) => RunAsync(async () =>
    {
        Refresh();
        await SetDefaultAsync(id);
    });

    private async Task SetDefaultAsync(string id)
    {
        if (!Snapshot.Devices.Any(d => SameId(d.Id, id) && d.Connected))
            throw new InvalidOperationException("This output is no longer connected. Choose another device.");
        backend.SetDefault(id, Settings.SwitchCommunications);
        // Core Audio's default-endpoint query can lag behind the successful policy call.
        // Yield to Windows/COM notifications rather than treating the first stale read as failure.
        for (int attempt = 0; attempt <= 20; attempt++)
        {
            Refresh();
            if (string.Equals(Snapshot.DefaultId, id, StringComparison.OrdinalIgnoreCase))
            {
                Switched?.Invoke(Current!);
                return;
            }
            if (attempt < 20) await (delay ?? Task.Delay)(TimeSpan.FromMilliseconds(100));
        }
        throw new InvalidOperationException("The selected output did not become the Windows default. Try switching again.");
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsSwitching) return; // Coalesce repeated hotkeys while Windows is still applying a switch.
        IsSwitching = true;
        Error = null;
        Changed?.Invoke();
        try { await action(); }
        catch (Exception ex) { Error = ex.Message; Failed?.Invoke(Error); throw; }
        finally { IsSwitching = false; Changed?.Invoke(); }
    }

    private static bool SameId(string? first, string? second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
