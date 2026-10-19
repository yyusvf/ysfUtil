using YsfUtil;

// The real shutdown action is always replaced. No Windows shutdown is scheduled by these tests.
var clock = new TestClock();
int shutdowns = 0;
int warnings = 0;
var timer = new ShutdownTimer(() => shutdowns++, clock);
timer.EndingSoon += () => warnings++;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

foreach (int invalid in new[] { -1, 0, 1441, int.MaxValue })
{
    try
    {
        timer.Start(invalid);
        throw new Exception("Accepted invalid duration: " + invalid);
    }
    catch (ArgumentOutOfRangeException) { }
}
Check(!timer.IsRunning, "Invalid input started a timer.");
timer.Start(2);
Check(timer.Remaining == TimeSpan.FromMinutes(2), "Incorrect initial duration.");
var originalDeadline = timer.Deadline;
try
{
    timer.Start(30);
    throw new Exception("A running timer was silently replaced.");
}
catch (InvalidOperationException) { }
Check(timer.Deadline == originalDeadline, "A duplicate start moved the deadline.");

clock.Advance(TimeSpan.FromSeconds(59));
timer.Tick();
Check(shutdowns == 0 && warnings == 0, "Shutdown or warning occurred too early.");
clock.Advance(TimeSpan.FromMilliseconds(500));
Check(timer.Remaining == TimeSpan.FromSeconds(61), "Remaining seconds should round up.");
clock.Advance(TimeSpan.FromMilliseconds(500));
timer.Tick();
timer.Tick();
Check(warnings == 1, "Final-minute warning must occur exactly once.");
clock.Advance(TimeSpan.FromSeconds(59));
timer.Tick();
Check(shutdowns == 0, "Shutdown occurred before the deadline.");
clock.Advance(TimeSpan.FromSeconds(1));
timer.Tick();
timer.Tick();
Check(shutdowns == 1 && !timer.IsRunning, "Shutdown must occur exactly once at expiry.");

timer.Start(1);
timer.Cancel();
clock.Advance(TimeSpan.FromDays(2));
timer.Tick();
Check(shutdowns == 1 && timer.Remaining == TimeSpan.Zero, "Cancelled timer fired.");

timer.Start(1440);
Check(timer.Remaining == TimeSpan.FromHours(24), "Maximum duration is incorrect.");
clock.Advance(TimeSpan.FromHours(25));
timer.Tick();
Check(shutdowns == 2 && !timer.IsRunning, "Missed deadline after sleep was not handled.");

int attempts = 0;
string? failure = null;
var failingTimer = new ShutdownTimer(() => { attempts++; throw new Exception("Test failure"); }, clock);
failingTimer.Failed += message => failure = message;
failingTimer.Start(1);
clock.Advance(TimeSpan.FromMinutes(1));
failingTimer.Tick();
failingTimer.Tick();
Check(attempts == 1 && !failingTimer.IsRunning && failure?.Contains("Test failure") == true,
    "Failed shutdown should report an error without retrying.");
failingTimer.Start(1);
Check(failingTimer.IsRunning, "Could not restart after failure.");
failingTimer.Cancel();

timer.Start(1);
clock.Advance(TimeSpan.FromSeconds(1));
timer.Tick();
Check(warnings == 2, "A new timer did not reset the warning.");
timer.Cancel();
Console.WriteLine("PASS: validation, duplicate start, countdown, warning, expiry, cancellation, sleep, failure and restart.");

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
