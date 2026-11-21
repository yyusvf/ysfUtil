using System.Diagnostics;
using System.IO;
using YsfUtil.Modules;
using YsfUtil.Ui.ViewModels;
using YsfUtil.Updates;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace YsfUtil.Plugins;

/// <summary>A plugin's row in Settings → Modules: installed version, what GitHub offers, actions.</summary>
internal sealed class PluginViewModel : ViewModelBase
{
    private readonly PluginManager manager;
    private InstalledPlugin? installed;
    private PluginRelease? available;
    private string? status;
    private bool busy;
    private bool needsRestart;

    public PluginViewModel(string id, PluginCatalogEntry? entry, InstalledPlugin? installed, PluginManager manager)
    {
        Id = id;
        Entry = entry;
        this.installed = installed;
        this.manager = manager;
        ActionCommand = new RelayCommand(async () => await manager.InstallAsync(this, enable: !IsInstalled));
        UninstallCommand = new RelayCommand(() => manager.Uninstall(this));
        PageCommand = new RelayCommand(OpenPage);
    }

    public string Id { get; }

    /// <summary>Null for a plugin installed by hand that ysfUtil doesn't list.</summary>
    public PluginCatalogEntry? Entry { get; }

    public InstalledPlugin? Installed
    {
        get => installed;
        set { installed = value; RaiseAll(); }
    }

    public PluginRelease? Available
    {
        get => available;
        set { available = value; RaiseAll(); }
    }

    public string? Status
    {
        get => status;
        set { status = value; RaiseAll(); }
    }

    public bool IsBusy
    {
        get => busy;
        set { busy = value; RaiseAll(); }
    }

    /// <summary>A newer version is installed but an older one is loaded.</summary>
    public bool NeedsRestart
    {
        get => needsRestart;
        set { needsRestart = value; RaiseAll(); }
    }

    public bool IsInstalled => installed != null;
    public bool HasUpdate => installed != null && available != null && available.Version > installed.Version;
    public bool CanInstall => !busy && Entry != null && (!IsInstalled || HasUpdate);
    public bool CanUninstall => !busy && IsInstalled;
    public string ActionLabel => !IsInstalled ? "Install" : $"Update to {available?.Version.ToString(3)}";

    /// <summary>Under the description: progress or error, else the version.</summary>
    public string? Detail => status ?? (needsRestart ? "Restart ysfUtil to use the new version."
        : installed != null ? "Version " + installed.Manifest.Version + (HasUpdate ? $" · {available!.Version.ToString(3)} available" : "")
        : available != null ? $"Version {available.Version.ToString(3)} · from {Entry?.Repository}"
        : Entry != null ? "From " + Entry.Repository : null);

    public RelayCommand ActionCommand { get; }
    public RelayCommand UninstallCommand { get; }
    public RelayCommand PageCommand { get; }

    private void OpenPage()
    {
        if (Entry == null) return;
        try { Process.Start(new ProcessStartInfo(available?.PageUrl ?? Entry.RepositoryUrl) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private void RaiseAll()
    {
        Raise(nameof(Installed)); Raise(nameof(Available)); Raise(nameof(Status)); Raise(nameof(IsBusy));
        Raise(nameof(NeedsRestart)); Raise(nameof(IsInstalled)); Raise(nameof(HasUpdate)); Raise(nameof(CanInstall));
        Raise(nameof(CanUninstall)); Raise(nameof(ActionLabel)); Raise(nameof(Detail));
        manager.OnPluginChanged();
    }
}

/// <summary>
/// Installs, updates and removes plugins - the browser behind Settings → Modules. Plugins come
/// from the GitHub releases of their apps (<see cref="PluginCatalog"/>), are verified like ysfUtil's
/// own updates, and are checked for updates on the same schedule.
/// </summary>
internal sealed class PluginManager : ViewModelBase, IDisposable
{
    private readonly ModuleHost host;
    private readonly PluginStore store;
    private readonly AppSettings settings;
    private readonly Action<string, string, ToolTipIcon> hint;
    private readonly Func<string?> restartBlocker;
    private readonly Action exit;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(1) };
    private string? restartError;

    public PluginManager(ModuleHost host, PluginStore store, AppSettings settings,
        Action<string, string, ToolTipIcon> hint, Func<string?> restartBlocker, Action exit)
    {
        this.host = host;
        this.store = store;
        this.settings = settings;
        this.hint = hint;
        this.restartBlocker = restartBlocker;
        this.exit = exit;
        foreach (ModuleViewModel entry in host.Entries.Where(e => PluginCatalog.Find(e.Info.Id) != null || store.Find(e.Info.Id) != null))
        {
            var plugin = new PluginViewModel(entry.Info.Id, PluginCatalog.Find(entry.Info.Id), store.Find(entry.Info.Id), this);
            entry.Plugin = plugin;
            Plugins.Add(plugin);
        }
        RestartCommand = new RelayCommand(Restart);
        timer.Tick += async (_, _) => await CheckAsync(manual: false);
    }

    public List<PluginViewModel> Plugins { get; } = [];

    public bool RestartRequired => Plugins.Any(p => p.NeedsRestart);

    public string RestartText => restartError ?? "Restart ysfUtil to finish updating plugins.";

    public RelayCommand RestartCommand { get; }

    /// <summary>Any plugin has a newer version than the installed one.</summary>
    public bool HasUpdate => Plugins.Any(p => p.HasUpdate);

    /// <summary>Catalog plugins plus any installed by hand, as Settings → Modules rows.</summary>
    public static IEnumerable<ModuleInfo> Modules(PluginStore store)
    {
        store.Cleanup();
        var installed = store.Installed().ToDictionary(p => p.Id);
        foreach (PluginCatalogEntry entry in PluginCatalog.All)
        {
            installed.Remove(entry.Id, out InstalledPlugin? plugin);
            yield return new(entry.Id, entry.Name, entry.Description, entry.Glyph, plugin == null ? null : Factory(plugin));
        }
        foreach (InstalledPlugin plugin in installed.Values.OrderBy(p => p.Manifest.Name))
        {
            PluginManifest m = plugin.Manifest;
            yield return new(m.Id, m.Name, m.Description, m.Glyph, Factory(plugin));
        }
    }

    private static Func<ModuleContext, IModule> Factory(InstalledPlugin plugin) =>
        context => PluginLoader.CreateModule(plugin, new PluginContext(context, plugin));

    /// <summary>After start: bring back plugins that were on before, then the scheduled update check.</summary>
    public async Task StartAsync()
    {
        foreach (PluginViewModel plugin in Plugins.Where(p => !p.IsInstalled && p.Entry != null && host.Entry(p.Id)?.WantsEnabled == true).ToList())
        {
            // Audio Mirror and DS5 used to be built in: whoever had them on gets them installed.
            await InstallAsync(plugin, enable: true, quiet: true);
        }
        timer.Start();
        await CheckAsync(manual: false);
    }

    public async Task CheckAsync(bool manual)
    {
        if (lifetime.IsCancellationRequested
            || (!manual && !UpdateChecker.ShouldCheck(settings.Updates, settings.LastPluginCheckUtc, DateTime.UtcNow))) return;
        foreach (PluginViewModel plugin in Plugins.Where(p => p.Entry != null && !p.IsBusy).ToList())
        {
            try
            {
                plugin.Available = await PluginFeed.LatestAsync(plugin.Entry!, lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or IOException or System.Text.Json.JsonException
                or TaskCanceledException or KeyNotFoundException or InvalidOperationException)
            {
                if (manual) plugin.Status = "Could not check GitHub.";
            }
        }
        if (!manual)
        {
            settings.LastPluginCheckUtc = DateTime.UtcNow;
            settings.Save();
        }
        if (settings.Updates == UpdateMode.Automatic)
        {
            foreach (PluginViewModel plugin in Plugins.Where(p => p.HasUpdate).ToList())
                await InstallAsync(plugin, enable: false, quiet: true);
        }
    }

    /// <summary>Installs or updates from GitHub. A fresh install from the button also turns the plugin on.</summary>
    public async Task InstallAsync(PluginViewModel plugin, bool enable, bool quiet = false)
    {
        if (plugin.IsBusy || plugin.Entry == null || lifetime.IsCancellationRequested) return;
        plugin.IsBusy = true;
        plugin.Status = "Checking GitHub…";
        string? zip = null;
        try
        {
            PluginRelease release = await PluginFeed.LatestAsync(plugin.Entry, lifetime.Token)
                ?? throw new InvalidOperationException("No release with this plugin yet.");
            plugin.Available = release;
            if (plugin.Installed != null && release.Version <= plugin.Installed.Version)
            {
                plugin.Status = null;
                return;
            }
            var progress = new Progress<int>(percent => plugin.Status = $"Downloading… {percent}%");
            zip = await PluginFeed.DownloadAsync(release, progress, lifetime.Token);
            plugin.Status = "Installing…";
            string path = zip;
            InstalledPlugin installed = await Task.Run(() => store.Install(path, plugin.Id));
            Activate(plugin, installed, enable);
            plugin.Status = null;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            plugin.Status = "Install failed: " + e.Message;
            if (quiet) hint(plugin.Entry.Name, plugin.Status, ToolTipIcon.Warning);
        }
        finally
        {
            if (zip != null) TryDelete(Path.GetDirectoryName(zip)!);
            plugin.IsBusy = false;
        }
    }

    /// <summary>For --install-plugin and tests: install a zip from disk.</summary>
    public void InstallFromFile(string zip)
    {
        InstalledPlugin installed = store.Install(zip);
        PluginViewModel? plugin = Plugins.FirstOrDefault(p => p.Id == installed.Id);
        if (plugin != null) Activate(plugin, installed, enable: !plugin.IsInstalled);
    }

    private void Activate(PluginViewModel plugin, InstalledPlugin installed, bool enable)
    {
        plugin.Installed = installed;
        if (PluginLoader.NeedsRestart(installed))
        {
            plugin.NeedsRestart = true;
            return;
        }
        if (host.Entry(plugin.Id) is { } entry)
        {
            if (enable) entry.WantsEnabled = true;
            host.SetFactory(plugin.Id, Factory(installed));
            settings.Save();
        }
    }

    public void Uninstall(PluginViewModel plugin)
    {
        if (!plugin.CanUninstall) return;
        host.SetFactory(plugin.Id, null);
        if (host.Entry(plugin.Id) is { } entry) entry.WantsEnabled = false;
        settings.Save();
        try { store.Uninstall(plugin.Id); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { plugin.Status = "Could not remove: " + e.Message; return; }
        plugin.NeedsRestart = false;
        plugin.Installed = null;
        plugin.Status = null;
    }

    private void Restart()
    {
        restartError = restartBlocker();
        Raise(nameof(RestartText));
        if (restartError != null) return;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (string argument in new[] { RestartArgument, Environment.ProcessId.ToString(), "--update-show" })
                start.ArgumentList.Add(argument);
            Process.Start(start);
            exit();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            restartError = "Could not restart: " + e.Message;
            Raise(nameof(RestartText));
        }
    }

    /// <summary>ysfUtil.exe --restart-after &lt;pid&gt;: waits for the old instance before starting.</summary>
    public const string RestartArgument = "--restart-after";

    public static void WaitForPrevious(string[] args)
    {
        int at = Array.IndexOf(args, RestartArgument);
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out int pid)) return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(15000);
        }
        catch (ArgumentException) { } // Already gone.
    }

    internal void OnPluginChanged()
    {
        Raise(nameof(RestartRequired));
        Raise(nameof(HasUpdate));
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        timer.Stop();
        lifetime.Cancel();
    }
}
