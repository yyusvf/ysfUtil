using System.IO;
using System.Text.Json;
using YsfUtil.Updates;

namespace YsfUtil.Plugins;

/// <summary>
/// The plugins ysfUtil offers. Each lives in its app's own repository; every release there carries
/// &lt;id&gt;.ysfplugin.zip next to the app, so updating the app updates the plugin too.
/// </summary>
internal sealed record PluginCatalogEntry(
    string Id,
    string Name,
    string Description,
    string Glyph,
    string Repository,
    string? LegacySettingsKey = null)
{
    public string AssetName => Id + ".ysfplugin.zip";
    public string RepositoryUrl => "https://github.com/" + Repository;
}

internal static class PluginCatalog
{
    public static IReadOnlyList<PluginCatalogEntry> All { get; } =
    [
        new("audio-mirror", "Audio Mirror",
            "Play Windows audio on several output devices at once.",
            "", "yyusvf/audio-mirror", LegacySettingsKey: "audioMirror"),
        new("ds5-haptics", "DS5 BT Haptics",
            "DualSense HD haptics, adaptive triggers and light bar over Bluetooth. Needs usbip-win2 and HidHide, set up from the flyout.",
            "", "yyusvf/ds5-bt-haptics", LegacySettingsKey: "ds5"),
    ];

    public static PluginCatalogEntry? Find(string id) => All.FirstOrDefault(e => e.Id == id);
}

internal sealed record PluginRelease(Version Version, string PageUrl, string DownloadUrl, long Size, string Sha256);

/// <summary>Finds and downloads plugin releases on GitHub, checked like ysfUtil's own updates.</summary>
internal static class PluginFeed
{
    /// <summary>The newest stable release that carries a verifiable plugin zip, or null.</summary>
    public static PluginRelease? Parse(string json, PluginCatalogEntry entry)
    {
        using var doc = JsonDocument.Parse(json);
        PluginRelease? best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || (best != null && version <= best.Version)) continue;
            string page = release.GetProperty("html_url").GetString() ?? "";
            if (!page.StartsWith(entry.RepositoryUrl + "/releases/", StringComparison.Ordinal)) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(), entry.AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
                long size = asset.GetProperty("size").GetInt64();
                if (!url.StartsWith(entry.RepositoryUrl + "/releases/download/", StringComparison.Ordinal)
                    || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit)
                    || size < 1024 || size > 200 * 1024 * 1024) continue;
                best = new(version, page, url, size, digest[7..]);
                break;
            }
        }
        return best;
    }

    public static async Task<PluginRelease?> LatestAsync(PluginCatalogEntry entry, CancellationToken token)
    {
        using var client = UpdateChecker.Client(TimeSpan.FromSeconds(15));
        string feed = $"https://api.github.com/repos/{entry.Repository}/releases?per_page=10";
        return Parse(await client.GetStringAsync(feed, token).ConfigureAwait(false), entry);
    }

    /// <summary>Downloads and verifies the zip; the caller deletes the returned file.</summary>
    public static async Task<string> DownloadAsync(PluginRelease release, IProgress<int>? progress, CancellationToken token)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ysfUtil-plugins", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "plugin.zip");
        try
        {
            await UpdateChecker.DownloadFileAsync(release.DownloadUrl, path, release.Size, progress, token).ConfigureAwait(false);
            await UpdateChecker.VerifyAsync(path, release.Size, release.Sha256, token, magic: "PK").ConfigureAwait(false);
            return path;
        }
        catch
        {
            Directory.Delete(folder, recursive: true);
            throw;
        }
    }
}
