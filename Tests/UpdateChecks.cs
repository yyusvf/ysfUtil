using System.Security.Cryptography;
using System.Text.Json;
using YsfUtil.Updates;

internal static class UpdateChecks
{
    public static async Task Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Check(!UpdateChecker.ShouldCheck(UpdateMode.Never, DateTime.MinValue, now), "Never mode made a background check.");
        Check(!UpdateChecker.ShouldCheck(UpdateMode.Notify, now.AddHours(-23), now), "Daily interval was not respected.");
        Check(UpdateChecker.ShouldCheck(UpdateMode.Automatic, now.AddDays(-1), now), "Daily update check skipped.");
        object Release(string tag, bool pre = false, bool draft = false, string? digest = null, string? url = null) => new
        {
            tag_name = tag, prerelease = pre, draft,
            html_url = UpdateChecker.Repository + "/releases/tag/" + tag,
            assets = new[] { new { name = "ysfUtil.exe", browser_download_url = url ?? UpdateChecker.Repository + "/releases/download/" + tag + "/ysfUtil.exe", size = 4096, digest = digest ?? "sha256:" + new string('a', 64) } },
        };
        string json = JsonSerializer.Serialize(new[] { Release("v1.9.0"), Release("v1.10.0"), Release("v9.0.0", pre: true), Release("v8.0.0", draft: true), Release("v12.0.0-beta") });
        var result = UpdateChecker.Parse(json, new(1, 0));
        Check(result?.Version == new Version(1, 10, 0, 0) && result.DownloadUrl != null, "Stable numeric version selection failed.");
        Check(UpdateChecker.Parse(JsonSerializer.Serialize(new[] { Release("v1.0.0") }), new(1, 0)) == null, "Equivalent versions caused an update loop.");
        Check(UpdateChecker.Parse(JsonSerializer.Serialize(new[] { Release("v1.0.0") }), new(2, 0)) == null, "Downgrade offered.");
        Check(UpdateChecker.Parse("[]", new(0, 1)) == null, "Empty releases failed.");
        result = UpdateChecker.Parse(JsonSerializer.Serialize(new[] { Release("v2.0.0", url: "https://other.example/ysfUtil.exe") }), new(1, 0));
        Check(result != null && result.DownloadUrl == null, "Foreign download accepted.");
        result = UpdateChecker.Parse(JsonSerializer.Serialize(new[] { Release("v2.0.0", digest: "sha256:bad") }), new(1, 0));
        Check(result != null && result.DownloadUrl == null, "Unverifiable asset accepted.");

        string folder = Path.Combine(Path.GetTempPath(), "ysfUtil-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string source = Path.Combine(folder, "new.exe"), target = Path.Combine(folder, "app.exe");
            byte[] data = new byte[4096]; data[0] = (byte)'M'; data[1] = (byte)'Z'; data[50] = 123;
            await File.WriteAllBytesAsync(source, data);
            string hash = Convert.ToHexString(SHA256.HashData(data));
            await UpdateChecker.VerifyAsync(source, data.Length, hash);
            async Task Reject(long size, string digest)
            {
                try { await UpdateChecker.VerifyAsync(source, size, digest); }
                catch (InvalidDataException) { return; }
                throw new Exception("Corrupt download accepted.");
            }
            await Reject(4095, hash);
            await Reject(4096, new string('0', 64));
            await File.WriteAllTextAsync(target, "previous version");
            PortableUpdater.Replace(source, target);
            Check(File.ReadAllBytes(target).SequenceEqual(data), "Portable replacement failed.");
            Check(File.ReadAllText(target + ".previous") == "previous version", "Rollback backup lost.");
            await File.WriteAllTextAsync(source, "next version");
            PortableUpdater.Replace(source, target);
            Check(File.ReadAllBytes(target + ".previous").SequenceEqual(data), "Repeated update backup failed.");
            try { PortableUpdater.Replace(Path.Combine(folder, "missing.exe"), target); } catch (FileNotFoundException) { }
            Check(File.ReadAllText(target) == "next version" && !File.Exists(target + ".incoming"), "Failed staging damaged installed app.");
            Check(PortableUpdater.Apply(["--apply-update", "invalid"]) == 1, "Invalid helper command accepted.");
        }
        finally { Directory.Delete(folder, recursive: true); }
        string cleanup = Path.Combine(Path.GetTempPath(), "ysfUtil-updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cleanup);
        File.WriteAllText(Path.Combine(cleanup, "ready"), "ready");
        File.WriteAllText(Path.Combine(cleanup, "helper.exe"), "test");
        await PortableUpdater.CleanupAsync(["--update-cleanup", cleanup + Path.DirectorySeparatorChar]);
        Check(!Directory.Exists(cleanup), "Temporary helper files were not cleaned after restart.");
        Console.WriteLine("PASS: update intervals, stable versions, release assets, checksums, atomic replacement, backup and failed staging.");
    }
}
