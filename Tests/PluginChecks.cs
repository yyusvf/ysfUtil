using System.IO.Compression;
using System.Text.Json;
using YsfUtil.Modules;
using YsfUtil.Plugins;

internal static class PluginChecks
{
    public static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string root = Path.Combine(Path.GetTempPath(), "ysfUtil-plugin-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PluginStore(Path.Combine(root, "store"));
            string Zip(string id, string version, int api = PluginApi.Version, bool withAssembly = true, string? name = null)
            {
                string folder = Path.Combine(root, "src-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, PluginApi.ManifestFile), JsonSerializer.Serialize(new
                {
                    id, name = name ?? "Test", description = "A test plugin.", glyph = "", version,
                    apiVersion = api, assembly = "Test.Plugin.dll", module = "Test.Plugin.Module",
                }));
                if (withAssembly) File.WriteAllText(Path.Combine(folder, "Test.Plugin.dll"), "not really an assembly");
                string zip = folder + ".zip";
                ZipFile.CreateFromDirectory(folder, zip);
                return zip;
            }

            InstalledPlugin first = store.Install(Zip("test", "1.0.0"), "test");
            Check(store.Find("test")?.Manifest.Version == "1.0.0" && Directory.Exists(first.Folder), "A plugin was not installed.");
            store.Install(Zip("test", "1.1.0"), "test");
            Check(store.Find("test")?.Manifest.Version == "1.1.0", "An update did not become current.");
            Check(Directory.Exists(first.Folder), "The old version was removed while it may still be loaded.");
            store.Cleanup();
            Check(!Directory.Exists(first.Folder) && store.Find("test") != null, "Cleanup kept the old version or lost the current one.");

            static bool Fails(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
            Check(Fails(() => store.Install(Zip("other", "1.0.0"), "test")), "A download replaced a different plugin.");
            Check(Fails(() => store.Install(Zip("test", "2.0.0", api: PluginApi.Version + 1), "test")), "A plugin for a newer ysfUtil was installed.");
            Check(Fails(() => store.Install(Zip("test", "2.0.0", withAssembly: false), "test")), "A plugin without its assembly was installed.");
            Check(Fails(() => store.Install(Zip("../evil", "2.0.0"))), "An invalid plugin id was accepted.");
            Check(store.Find("test")?.Manifest.Version == "1.1.0", "A failed install changed the installed plugin.");
            Check(Directory.GetDirectories(store.Root).All(d => !Path.GetFileName(d).StartsWith('.')), "A failed install left files behind.");
            Check(store.Find("test", "../../x") == null, "A version outside the plugin folder was resolved.");

            store.Uninstall("test");
            Check(store.Find("test") == null && store.Installed().Count == 0, "Uninstall left the plugin installed.");
            store.Cleanup();
            Check(!Directory.Exists(Path.Combine(store.Root, "test")), "Cleanup kept an uninstalled plugin.");

            var entry = new PluginCatalogEntry("audio-mirror", "Audio Mirror", "", "", "yyusvf/audio-mirror");
            object Release(string tag, string asset = "audio-mirror.ysfplugin.zip", string? url = null, string? digest = null, bool pre = false) => new
            {
                tag_name = tag, prerelease = pre, draft = false,
                html_url = entry.RepositoryUrl + "/releases/tag/" + tag,
                assets = new[] { new { name = asset, browser_download_url = url ?? entry.RepositoryUrl + "/releases/download/" + tag + "/" + asset, size = 4096, digest = digest ?? "sha256:" + new string('b', 64) } },
            };
            string Feed(params object[] releases) => JsonSerializer.Serialize(releases);
            Check(PluginFeed.Parse(Feed(Release("v1.4.0"), Release("v1.10.0"), Release("v2.0.0", pre: true)), entry)?.Version == new Version(1, 10, 0),
                "The newest stable plugin release was not chosen.");
            Check(PluginFeed.Parse(Feed(Release("v1.5.0", asset: "AudioMirror-Setup.exe"), Release("v1.4.0")), entry)?.Version == new Version(1, 4, 0),
                "A release without the plugin was offered.");
            Check(PluginFeed.Parse(Feed(Release("v1.5.0", url: "https://other.example/audio-mirror.ysfplugin.zip")), entry) == null,
                "A foreign download was accepted.");
            Check(PluginFeed.Parse(Feed(Release("v1.5.0", digest: "sha256:bad")), entry) == null, "An unverifiable plugin was accepted.");
            Check(PluginFeed.Parse("[]", entry) == null, "Empty releases failed.");
            Check(PluginCatalog.All.All(e => PluginCatalog.Find(e.Id) == e && e.AssetName == e.Id + ".ysfplugin.zip"), "The catalog is inconsistent.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        Console.WriteLine("PASS: plugin install, update, cleanup, uninstall, wrong id, API version, missing assembly, release selection and verification.");
    }
}
