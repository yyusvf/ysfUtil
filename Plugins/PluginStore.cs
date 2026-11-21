using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using YsfUtil.Modules;

namespace YsfUtil.Plugins;

/// <summary>plugin.json at the root of a plugin folder (and of its release zip).</summary>
internal sealed record PluginManifest(
    string Id,
    string Name,
    string Description,
    string Glyph,
    string Version,
    int ApiVersion,
    string Assembly,
    string Module,
    string? Process = null)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Version ParsedVersion => System.Version.Parse(Version);

    /// <summary>Reads and checks a manifest; throws <see cref="InvalidDataException"/> with the reason.</summary>
    public static PluginManifest Read(string folder)
    {
        string path = Path.Combine(folder, PluginApi.ManifestFile);
        if (!File.Exists(path)) throw new InvalidDataException("The plugin has no plugin.json.");
        PluginManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), Json); }
        catch (JsonException e) { throw new InvalidDataException("plugin.json is not readable: " + e.Message); }
        if (manifest == null) throw new InvalidDataException("plugin.json is empty.");
        manifest.Check(folder);
        return manifest;
    }

    private void Check(string folder)
    {
        if (!Regex.IsMatch(Id ?? "", "^[a-z0-9][a-z0-9-]{0,39}$")) throw new InvalidDataException("The plugin id is invalid.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("The plugin has no name.");
        if (!System.Version.TryParse(Version, out _)) throw new InvalidDataException("The plugin version is invalid.");
        if (ApiVersion != PluginApi.Version)
            throw new InvalidDataException(ApiVersion > PluginApi.Version
                ? "This plugin needs a newer ysfUtil. Update ysfUtil first."
                : "This plugin was built for an older ysfUtil and needs an update.");
        if (string.IsNullOrEmpty(Assembly) || Path.GetFileName(Assembly) != Assembly || !File.Exists(Path.Combine(folder, Assembly)))
            throw new InvalidDataException("The plugin's assembly is missing.");
        if (string.IsNullOrWhiteSpace(Module)) throw new InvalidDataException("The plugin names no module.");
    }
}

internal sealed record InstalledPlugin(PluginManifest Manifest, string Folder)
{
    public string Id => Manifest.Id;
    public Version Version => Manifest.ParsedVersion;
}

/// <summary>
/// Installed plugins in %LOCALAPPDATA%\ysfUtil\plugins: &lt;id&gt;\&lt;version&gt;\ holds a plugin's files,
/// &lt;id&gt;\current names the version to load. A new version goes into its own folder, so it can be
/// installed while the old one is loaded; the old folder is removed on the next start.
/// </summary>
internal sealed class PluginStore(string root)
{
    private const string CurrentFile = "current";

    /// <summary>YSFUTIL_PLUGINS points elsewhere, e.g. to try a plugin build without touching the real one.</summary>
    public static PluginStore Default { get; } = new(Environment.GetEnvironmentVariable("YSFUTIL_PLUGINS") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ysfUtil", "plugins"));

    public string Root => root;

    public IReadOnlyList<InstalledPlugin> Installed()
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root)
            .Select(folder => Find(Path.GetFileName(folder)))
            .OfType<InstalledPlugin>()
            .ToList();
    }

    /// <summary>The installed version of a plugin, or null when missing or broken.</summary>
    public InstalledPlugin? Find(string id)
    {
        try
        {
            string current = Path.Combine(root, id, CurrentFile);
            return File.Exists(current) ? Find(id, File.ReadAllText(current).Trim()) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public InstalledPlugin? Find(string id, string version)
    {
        if (id.Contains('.') || version.Contains("..") || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string folder = Path.Combine(root, id, version);
        try
        {
            var manifest = PluginManifest.Read(folder);
            return manifest.Id == id && manifest.Version == version ? new(manifest, folder) : null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Unpacks a plugin zip and makes it the current version. <paramref name="expectedId"/>, when
    /// given, must match the manifest - a download for one plugin can't replace another.
    /// </summary>
    public InstalledPlugin Install(string zipPath, string? expectedId = null)
    {
        Directory.CreateDirectory(root);
        string staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            // ExtractToDirectory refuses entries that would land outside the folder.
            ZipFile.ExtractToDirectory(zipPath, staging);
            PluginManifest manifest = PluginManifest.Read(staging);
            if (expectedId != null && manifest.Id != expectedId)
                throw new InvalidDataException($"The download contains “{manifest.Id}” instead of “{expectedId}”.");
            string target = Path.Combine(root, manifest.Id, manifest.Version);
            if (PluginLoader.IsLoaded(manifest.Id, manifest.Version))
            {
                // That exact version is running; its files are in use and identical anyway.
            }
            else
            {
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(staging, target);
            }
            string current = Path.Combine(root, manifest.Id, CurrentFile);
            File.WriteAllText(current + ".tmp", manifest.Version);
            File.Move(current + ".tmp", current, overwrite: true);
            return new(manifest, target);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Removes a plugin. Its files are deleted on the next start, since they may be loaded.</summary>
    public void Uninstall(string id)
    {
        string current = Path.Combine(root, id, CurrentFile);
        if (File.Exists(current)) File.Delete(current);
    }

    /// <summary>On start, before anything is loaded: drop old versions, uninstalled plugins and leftovers.</summary>
    public void Cleanup()
    {
        if (!Directory.Exists(root)) return;
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(folder);
            InstalledPlugin? keep = name.StartsWith('.') ? null : Find(name);
            try
            {
                if (keep == null)
                {
                    Directory.Delete(folder, recursive: true);
                    continue;
                }
                foreach (string version in Directory.EnumerateDirectories(folder))
                {
                    if (!string.Equals(version, keep.Folder, StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(version, recursive: true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// Loads plugin assemblies, each into a context of its own: a plugin brings its own dependencies
/// in whatever version it needs. YsfUtil.Sdk, and libraries ysfUtil already has in a sufficient
/// version, are shared, so both sides see the same types. Contexts are never unloaded - WPF keeps loaded views alive,
/// so a new version takes effect after a restart.
/// </summary>
internal static class PluginLoader
{
    private static readonly Dictionary<string, (InstalledPlugin Plugin, Assembly Assembly)> loaded = new(StringComparer.Ordinal);

    public static bool IsLoaded(string id, string version) =>
        loaded.TryGetValue(id, out var entry) && entry.Plugin.Manifest.Version == version;

    /// <summary>A different version of the plugin is already loaded in this process.</summary>
    public static bool NeedsRestart(InstalledPlugin plugin) =>
        loaded.TryGetValue(plugin.Id, out var entry) && entry.Plugin.Manifest.Version != plugin.Manifest.Version;

    public static IModule CreateModule(InstalledPlugin plugin, IModuleContext context) =>
        Create<IModule>(plugin, plugin.Manifest.Module, context);

    public static IPluginProcess CreateProcess(InstalledPlugin plugin) =>
        Create<IPluginProcess>(plugin, plugin.Manifest.Process ?? throw new InvalidOperationException("The plugin has no process entry."));

    private static T Create<T>(InstalledPlugin plugin, string typeName, params object[] arguments)
    {
        Type type;
        try
        {
            type = Load(plugin).GetType(typeName, throwOnError: true)!;
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            throw new InvalidOperationException($"The plugin could not be loaded: {e.Message}", e);
        }
        if (!typeof(T).IsAssignableFrom(type)) throw new InvalidOperationException($"“{typeName}” is not a {typeof(T).Name}.");
        try
        {
            return (T)Activator.CreateInstance(type, arguments)!;
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException);
            throw;
        }
    }

    private static Assembly Load(InstalledPlugin plugin)
    {
        if (loaded.TryGetValue(plugin.Id, out var entry))
        {
            if (entry.Plugin.Manifest.Version != plugin.Manifest.Version)
                throw new InvalidOperationException("Restart ysfUtil to finish updating this plugin.");
            return entry.Assembly;
        }
        var context = new PluginLoadContext(plugin);
        Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(plugin.Folder, plugin.Manifest.Assembly));
        loaded[plugin.Id] = (plugin, assembly);
        return assembly;
    }

    private sealed class PluginLoadContext(InstalledPlugin plugin) : AssemblyLoadContext("plugin:" + plugin.Id)
    {
        private static readonly string Sdk = typeof(IModule).Assembly.GetName().Name!;

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == Sdk) return null; // Always ysfUtil's own.
            // A library ysfUtil ships too (NAudio, ...) is shared when its version suffices: COM
            // interop types loaded twice can't be cast to each other.
            try
            {
                Assembly shared = Default.LoadFromAssemblyName(new AssemblyName(name.Name!));
                if (name.Version == null || shared.GetName().Version >= name.Version) return shared;
            }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException) { }
            string path = Path.Combine(plugin.Folder, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string name)
        {
            string path = Path.Combine(plugin.Folder, name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll");
            return File.Exists(path) ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }
    }
}

/// <summary>ysfUtil.exe --plugin-process &lt;id&gt; &lt;version&gt; ...: runs a plugin's <see cref="IPluginProcess"/>.</summary>
internal static class PluginProcess
{
    public const string Argument = "--plugin-process";

    public static IEnumerable<string> Arguments(InstalledPlugin plugin) => [Argument, plugin.Id, plugin.Manifest.Version];

    public static bool IsRequested(string[] args) => args.Length >= 3 && args[0] == Argument;

    public static int Run(string[] args)
    {
        InstalledPlugin? plugin = PluginStore.Default.Find(args[1], args[2]);
        if (plugin == null) return 2;
        return PluginLoader.CreateProcess(plugin).Run(args[3..]);
    }
}
