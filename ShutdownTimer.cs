using System.Diagnostics;
using System.IO;

namespace YsfUtil;

/// <summary>An in-memory deadline. Closing the app cancels it; only Tick can request shutdown.</summary>
internal sealed class ShutdownTimer
{
    private readonly TimeProvider clock;
    private readonly Action shutdown;
    private bool warned;

    public ShutdownTimer(Action? shutdown = null, TimeProvider? clock = null)
    {
        this.shutdown = shutdown ?? ShutdownWindows;
        this.clock = clock ?? TimeProvider.System;
    }

    public event Action? Changed;
    public event Action? EndingSoon;
    public event Action<string>? Failed;

    public DateTimeOffset? Deadline { get; private set; }
    public bool IsRunning => Deadline.HasValue;
    public TimeSpan Remaining => Deadline is { } deadline
        ? TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling((deadline - clock.GetUtcNow()).TotalSeconds)))
        : TimeSpan.Zero;

    public void Start(int minutes)
    {
        if (minutes is < 1 or > 1440)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), "Choose between 1 and 1440 minutes.");
        }
        if (IsRunning)
        {
            throw new InvalidOperationException("Cancel the current timer before starting another one.");
        }

        Deadline = clock.GetUtcNow().AddMinutes(minutes);
        warned = false;
        Changed?.Invoke();
    }

    public void Cancel()
    {
        Deadline = null;
        warned = false;
        Changed?.Invoke();
    }

    public void Tick()
    {
        if (!IsRunning) return;

        if (Remaining > TimeSpan.Zero)
        {
            if (!warned && Remaining <= TimeSpan.FromMinutes(1))
            {
                warned = true;
                EndingSoon?.Invoke();
            }
            Changed?.Invoke();
            return;
        }

        // Clear first so a failed shutdown cannot retry every second.
        Cancel();
        try
        {
            shutdown();
        }
        catch (Exception ex)
        {
            Failed?.Invoke("Shutdown failed: " + ex.Message);
        }
    }

    private static void ShutdownWindows()
    {
        // A positive shutdown.exe timeout implies /f. Keep the countdown in the app and use
        // /t 0 without /f so programs can still block shutdown to protect unsaved work.
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
            Arguments = "/s /t 0",
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Windows could not start shutdown.exe.");

        if (!process.WaitForExit(5000))
        {
            throw new InvalidOperationException("Windows has not confirmed the shutdown request.");
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Windows returned error {process.ExitCode}.");
        }
    }
}
