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
        foreach (var device in Snapshot.Devices)
        {
            var saved = Settings.Devices.FirstOrDefault(d => SameId(d.Id, device.Id));
            if (saved == null)
            {
                Settings.Devices.Add(new AudioOutputPreference
                {
                    Id = device.Id, Name = device.Name,
                    Included = !Settings.SelectionConfigured && device.Connected,
                });
            }
            else saved.Name = device.Name;
        }
        Changed?.Invoke();
    }

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
