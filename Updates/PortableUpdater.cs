using System.Diagnostics;
using System.IO;

namespace YsfUtil.Updates;

internal static class PortableUpdater
{
    public static async Task PrepareAsync(string source, string hash, bool quiet, CancellationToken token)
    {
        // The copied single-file app acts as its own helper, before WPF and the instance mutex.
#pragma warning disable IL3000 // Intentionally distinguish published single-file apps from loose development builds.
        if (!string.IsNullOrEmpty(typeof(PortableUpdater).Assembly.Location))
            throw new InvalidOperationException("Updates require the published portable EXE. Use a release build from dist.");
#pragma warning restore IL3000
        string target = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate ysfUtil.exe.");
        string folder = Path.GetDirectoryName(source)!;
        // Fail while the running application can still explain an unwritable destination.
        string probe = Path.Combine(Path.GetDirectoryName(target)!, ".ysfUtil-update-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { }
        File.Delete(probe);
        string helper = Path.Combine(folder, "helper.exe");
        File.Copy(target, helper);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
        foreach (string arg in new[] { "--apply-update", Environment.ProcessId.ToString(), target, hash, quiet ? "--tray" : "--show" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
        string ready = Path.Combine(folder, "ready");
        for (int attempt = 0; attempt < 100; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(ready))
            {
                token.ThrowIfCancellationRequested();
                File.WriteAllText(Path.Combine(folder, "install"), "install");
                return;
            }
            if (process.HasExited) throw new IOException("The update helper could not start. ysfUtil is still running.");
            await Task.Delay(100, token);
        }
        throw new IOException("The update helper did not respond. ysfUtil is still running.");
    }

    public static bool IsHelper(string[] args) => args.FirstOrDefault() == "--apply-update";

    public static int Apply(string[] args)
    {
        string? target = null;
        bool replaced = false;
        bool parentExited = false;
        string folder = AppContext.BaseDirectory;
        try
        {
            if (args.Length != 5 || !int.TryParse(args[1], out int pid)) return 1;
            target = Path.GetFullPath(args[2]);
            string source = Path.Combine(folder, "update.exe");
            using var parent = Process.GetProcessById(pid);
            if (!string.Equals(parent.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) return 1;
            UpdateChecker.VerifyAsync(source, new FileInfo(source).Length, args[3]).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(folder, "ready"), "ready");
            if (!parent.WaitForExit(60000)) return 1; // Never replace files while the app is still running.
            parentExited = true;
            if (!File.Exists(Path.Combine(folder, "install"))) return 1;
            Replace(source, target);
            replaced = true;
            Start(target, args[4] == "--tray", folder, failed: false);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString());
                if (target != null && parentExited)
                {
                    if (replaced) File.Copy(target + ".previous", target, overwrite: true);
                    Start(target, args.LastOrDefault() == "--tray", folder, failed: true);
                }
            }
            catch { /* The backup and error log remain available for manual recovery. */ }
            return 1;
        }
    }

    internal static void Replace(string source, string target)
    {
        // Stage on the destination volume, then atomically exchange it with a recoverable backup.
        string staged = target + ".incoming";
        try
        {
            File.Copy(source, staged, overwrite: true);
            for (int attempt = 0; ; attempt++)
            {
                try { File.Replace(staged, target, target + ".previous", ignoreMetadataErrors: true); return; }
                catch (IOException) when (attempt < 19) { Thread.Sleep(250); }
            }
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    private static void Start(string target, bool quiet, string folder, bool failed)
    {
        var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
        start.ArgumentList.Add("--tray");
        if (!quiet) start.ArgumentList.Add("--update-show");
        if (failed) start.ArgumentList.Add("--update-failed");
        else { start.ArgumentList.Add("--update-cleanup"); start.ArgumentList.Add(folder); }
        Process.Start(start)?.Dispose();
    }

    public static async Task CleanupAsync(string[] args)
    {
        int at = Array.IndexOf(args, "--update-cleanup");
        if (at < 0 || at + 1 >= args.Length) return;
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[at + 1]));
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ysfUtil-updates"));
        if (!string.Equals(Path.GetDirectoryName(folder), root, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(folder), "N", out _) || !File.Exists(Path.Combine(folder, "ready"))) return;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                foreach (string name in new[] { "helper.exe", "update.exe", "ready", "install" }) File.Delete(Path.Combine(folder, name));
                Directory.Delete(folder); // Never delete arbitrary nested contents.
                return;
            }
            catch { await Task.Delay(250); }
        }
    }
}
