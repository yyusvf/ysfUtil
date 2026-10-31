using YsfUtil.Audio;

internal static class AudioOutputChecks
{
    public static async Task Run()
    {
        var backend = new FakeBackend();
        var settings = new AudioOutputSettings();
        var audio = new AudioOutputSwitcher(backend, settings, _ => Task.CompletedTask);
        int changes = 0;
        audio.Switched += _ => changes++;
        audio.Refresh();
        Check(settings.Devices.Count == 3, "Device discovery failed.");
        Check(!settings.Devices.Single(d => d.Id == "offline").Included, "Unplugged device was selected automatically.");
        await audio.CycleAsync(); Check(backend.Current == "headphones", "Cycle did not move to the next output.");
        await audio.CycleAsync(); Check(backend.Current == "speakers", "Cycle did not wrap.");
        settings.SwitchCommunications = false;
        await audio.SelectAsync("headphones"); Check(!backend.Communications, "Communication opt-out was ignored.");
        backend.Current = "other";
        await audio.CycleAsync(); Check(backend.Current == "speakers", "An unselected current output should start the cycle at its first device.");
        backend.DisconnectHeadphones = true;
        settings.Devices.Single(d => d.Id == "offline").Included = true;
        await audio.CycleAsync(); Check(backend.Current == "speakers", "Cycle did not skip unplugged endpoints.");
        int before = backend.Switches;
        foreach (var item in settings.Devices) item.Included = false;
        await ExpectFailure(audio.CycleAsync);
        Check(backend.Switches == before && audio.Error != null, "Empty selection changed an output or hid its error.");
        await ExpectFailure(() => audio.SelectAsync("offline"));
        Check(backend.Switches == before, "Direct selection allowed an offline device.");
        backend.DisconnectHeadphones = false;
        backend.Fail = true;
        int notifications = changes;
        await ExpectFailure(() => audio.SelectAsync("headphones"));
        Check(audio.Snapshot.DefaultId == "speakers" && changes == notifications, "Failed switch changed the reported output or showed success.");
        backend.Fail = false;
        await audio.SelectAsync("headphones"); Check(audio.Error == null, "Successful retry did not clear the error.");
        settings.SelectionConfigured = true;
        backend.NewDevice = true;
        audio.Refresh();
        Check(!settings.Devices.Single(d => d.Id == "new").Included, "A new endpoint silently joined a configured cycle.");
        backend.DisconnectHeadphones = true;
        audio.Refresh();
        Check(settings.Devices.Any(d => d.Id == "headphones"), "Unplugging lost the stored selection.");

        // Windows re-creates an endpoint with a new id and "2- " in its name: one entry, selection kept.
        var renamedBackend = new FakeBackend();
        var renamedSettings = new AudioOutputSettings();
        var renamed = new AudioOutputSwitcher(renamedBackend, renamedSettings, _ => Task.CompletedTask);
        renamed.Refresh();
        renamedSettings.SelectionConfigured = true;
        renamedBackend.RenamedHeadphones = true;
        renamed.Refresh();
        Check(renamedSettings.Devices.Count(d => AudioOutputSwitcher.SameDevice(d.Name, "Headphones (USB)")) == 1
            && renamedSettings.Devices.Single(d => d.Id == "headphones-2").Included, "A re-created endpoint was listed twice or lost its selection.");
        // An unplugged twin of a connected output is not listed; a gone, unselected output is forgotten.
        renamedBackend.StaleTwin = true;
        renamed.Refresh();
        Check(renamed.Listed.Count(d => AudioOutputSwitcher.SameDevice(d.Name, "Speakers")) == 1, "An unplugged duplicate was listed.");
        renamedBackend.StaleTwin = false;
        renamedSettings.Devices.Single(d => d.Id == "offline").Included = false;
        renamedBackend.OfflineGone = true;
        renamed.Refresh();
        Check(renamedSettings.Devices.All(d => d.Id != "offline" && d.Id != "speakers-old"), "Gone outputs were kept without being selected.");
        var delayed = new FakeBackend { DelayedReads = 3 };
        int waits = 0, delayedNotifications = 0;
        var delayedAudio = new AudioOutputSwitcher(delayed, new(), _ => { waits++; return Task.CompletedTask; });
        delayedAudio.Switched += _ => delayedNotifications++;
        await delayedAudio.SelectAsync("headphones");
        Check(waits == 2 && delayedAudio.Error == null && delayedNotifications == 1,
            "A delayed Windows default update was incorrectly rejected.");

        var neverApplies = new FakeBackend { NeverApply = true };
        int failures = 0, falseSuccesses = 0;
        var rejectedAudio = new AudioOutputSwitcher(neverApplies, new(), _ => Task.CompletedTask);
        rejectedAudio.Failed += _ => failures++;
        rejectedAudio.Switched += _ => falseSuccesses++;
        await ExpectFailure(() => rejectedAudio.SelectAsync("headphones"));
        Check(failures == 1 && falseSuccesses == 0 && !rejectedAudio.IsSwitching,
            "An unapplied switch must fail once without showing success or remaining busy.");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busyBackend = new FakeBackend { DelayedReads = 2 };
        var busyAudio = new AudioOutputSwitcher(busyBackend, new(), _ => gate.Task);
        Task pending = busyAudio.SelectAsync("headphones");
        Check(busyAudio.IsSwitching && !pending.IsCompleted, "Pending confirmation should yield without blocking.");
        await busyAudio.SelectAsync("speakers");
        Check(busyBackend.Switches == 1, "Rapid repeated input started overlapping switches.");
        gate.SetResult();
        await pending;
        Check(!busyAudio.IsSwitching && busyAudio.Current?.Id == "headphones", "Pending confirmation did not finish.");
        busyBackend.Current = "HEADPHONES";
        busyAudio.Refresh();
        Check(busyAudio.Current?.Id == "headphones", "Endpoint IDs must be compared case-insensitively.");
        Console.WriteLine("PASS: audio cycling, wraparound, disconnection, direct selection, communication option, errors, retry, remembered devices, re-created endpoints and duplicates.");
        Console.WriteLine("PASS: delayed confirmation, real rejection, nonblocking wait and rapid repeated hotkeys.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task ExpectFailure(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected the operation to fail.");
    }

    private sealed class FakeBackend : IAudioOutputBackend
    {
        public string Current = "speakers";
        public bool Communications, DisconnectHeadphones, NewDevice, Fail, RenamedHeadphones, StaleTwin, OfflineGone;
        public int Switches;
        public int DelayedReads;
        public bool NeverApply;
        private string? pendingId;
        private int pendingReads;
        public AudioOutputSnapshot Read()
        {
            if (pendingId != null && --pendingReads <= 0)
            {
                Current = pendingId;
                pendingId = null;
            }
            var devices = new List<AudioOutputDevice>
            {
                new("speakers", "Speakers", true),
                RenamedHeadphones ? new("headphones-2", "Headphones (2- USB)", !DisconnectHeadphones)
                    : new("headphones", "Headphones (USB)", !DisconnectHeadphones),
            };
            if (!OfflineGone) devices.Add(new("offline", "Disconnected display", false));
            if (StaleTwin) devices.Add(new("speakers-old", "Speakers", false));
            if (NewDevice) devices.Add(new("new", "New output", true));
            return new(devices, Current);
        }
        public void SetDefault(string id, bool communications)
        {
            if (Fail) throw new InvalidOperationException("Device disappeared.");
            Switches++; Communications = communications;
            if (NeverApply) return;
            if (DelayedReads > 0) { pendingId = id; pendingReads = DelayedReads; }
            else Current = id;
        }
    }
}
