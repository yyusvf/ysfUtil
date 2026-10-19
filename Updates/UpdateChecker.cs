using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace YsfUtil.Updates;

internal enum UpdateMode { Notify, Automatic, Never }

internal sealed record UpdateInfo(Version Version, string PageUrl, string? DownloadUrl, long Size, string? Sha256, bool IsSetup = false);

// Adapted from Audio Mirror: stable GitHub releases, numeric comparison and daily checks.
internal static class UpdateChecker
{
    internal const string Repository = "https://github.com/yyusvf/ysfUtil";
    internal const string Feed = "https://api.github.com/repos/yyusvf/ysfUtil/releases?per_page=20";
    public static Version CurrentVersion => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new(0, 1, 0));
    public static bool ShouldCheck(UpdateMode mode, DateTime lastCheckUtc, DateTime nowUtc) =>
        mode != UpdateMode.Never && nowUtc - lastCheckUtc >= TimeSpan.FromDays(1);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static UpdateInfo? Parse(string json, Version current, bool installed = false)
    {
        using var doc = JsonDocument.Parse(json);
        UpdateInfo? best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) continue;
            Version version = Normalize(parsed);
            if (version <= Normalize(current) || (best != null && version <= best.Version)) continue;
            string page = release.GetProperty("html_url").GetString() ?? "";
            if (!page.StartsWith(Repository + "/releases/", StringComparison.Ordinal)) continue;
            string? url = null, hash = null;
            long size = 0;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string expected = installed ? "ysfUtil-Setup.exe" : "ysfUtil.exe";
                if (!name.Equals(expected, StringComparison.OrdinalIgnoreCase)) continue;
                string candidate = asset.GetProperty("browser_download_url").GetString() ?? "";
                string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
                long length = asset.GetProperty("size").GetInt64();
                if (!candidate.StartsWith(Repository + "/releases/download/", StringComparison.Ordinal)
                    || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit)
                    || length < 1024 || length > 100 * 1024 * 1024) continue;
                url = candidate;
                hash = digest[7..];
                size = length;
                break;
            }
            best = new(version, page, url, size, hash, IsSetup: installed);
        }
        return best;
    }

    private static HttpClient Client(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ysfUtil", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    // Errors deliberately propagate so a manual check never reports a failed request as "up to date".
    public static async Task<UpdateInfo?> FindNewerAsync(CancellationToken token)
    {
        using var client = Client(TimeSpan.FromSeconds(15));
        return Parse(await client.GetStringAsync(Feed, token).ConfigureAwait(false), CurrentVersion, Installation.IsInstalled);
    }

    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int> progress, CancellationToken token)
    {
        if (update.DownloadUrl == null || update.Sha256 == null) throw new InvalidOperationException("This release has no verified update for your installation.");
        string folder = Path.Combine(Path.GetTempPath(), "ysfUtil-updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "update.exe");
        try
        {
            using var client = Client(TimeSpan.FromMinutes(10));
            await using (var source = await client.GetStreamAsync(update.DownloadUrl, token).ConfigureAwait(false))
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > update.Size) throw new InvalidDataException("Update size did not match the release.");
                    await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    progress.Report((int)(total * 100 / update.Size));
                }
            }
            await VerifyAsync(path, update.Size, update.Sha256, token).ConfigureAwait(false);
            return path;
        }
        catch { File.Delete(path); throw; }
    }

    internal static async Task VerifyAsync(string path, long size, string hash, CancellationToken token = default)
    {
        await using var file = File.OpenRead(path);
        if (file.Length != size || file.ReadByte() != 'M' || file.ReadByte() != 'Z')
            throw new InvalidDataException("Downloaded update is incomplete or invalid.");
        file.Position = 0;
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
        if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum did not match. Please try again.");
    }
}
