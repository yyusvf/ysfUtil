using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Modules;

/// <summary>Settings of a single module.</summary>
internal sealed class ModuleSettings
{
    public bool Enabled { get; set; }
}

/// <summary>
/// A row in Settings → Modules: a built-in module or a plugin. <paramref name="Create"/> is null
/// while a plugin isn't installed.
/// </summary>
internal sealed record ModuleInfo(
    string Id,
    string Name,
    string Description,
    string Glyph,
    Func<ModuleContext, IModule>? Create,
    bool EnabledByDefault = false);

/// <summary>
/// Creates the enabled modules and tears them down again when they are switched off. Modules are
/// started in catalog order, which is also the order of their cards within a category.
/// </summary>
internal sealed class ModuleHost : IDisposable
{
    private readonly ModuleContext context;
    private readonly Dictionary<string, IModule> running = new(StringComparer.OrdinalIgnoreCase);

    public ModuleHost(ModuleContext context, IEnumerable<ModuleInfo> modules)
    {
        this.context = context;
        Entries = modules.Select(info => new ModuleViewModel(info, context.Settings.For(info), this)).ToList();
    }

    /// <summary>A module started or stopped.</summary>
    public event Action? Changed;

    /// <summary>A running module's <see cref="IModule.HasVisibleContent"/> changed.</summary>
    public event Action? ContentChanged;

    /// <summary>Starting a module failed: name and reason.</summary>
    public event Action<string, string>? Failed;

    public IReadOnlyList<ModuleViewModel> Entries { get; }

    /// <summary>Running modules in catalog order.</summary>
    public IEnumerable<IModule> Running =>
        Entries.Select(e => running.GetValueOrDefault(e.Info.Id)).OfType<IModule>();

    /// <summary>The running module with this id, or null.</summary>
    public IModule? Find(string id) => running.GetValueOrDefault(id);

    public ModuleViewModel? Entry(string id) => Entries.FirstOrDefault(e => e.Info.Id == id);

    public IEnumerable<FeatureViewModel> Features => Running.SelectMany(m => m.Features);

    public bool HasVisibleContent => Running.Any(m => m.HasVisibleContent);

    public string? UpdateBlocker => Running.Select(m => m.UpdateBlocker).FirstOrDefault(b => b != null);

    public void StartEnabled()
    {
        foreach (ModuleViewModel entry in Entries.Where(e => e.IsEnabled))
        {
            Start(entry, raise: false);
        }
        Changed?.Invoke();
    }

    /// <summary>A plugin was installed (factory) or removed (null). Starts or stops it as enabled.</summary>
    internal void SetFactory(string id, Func<ModuleContext, IModule>? create)
    {
        if (Entry(id) is not { } entry) return;
        Stop(id);
        entry.Create = create;
        if (entry.IsEnabled) Start(entry, raise: true);
    }

    internal void Apply(ModuleViewModel entry)
    {
        if (entry.IsEnabled) Start(entry, raise: true);
        else Stop(entry.Info.Id);
        context.Settings.Save();
    }

    private void Start(ModuleViewModel entry, bool raise)
    {
        if (entry.Create == null || running.ContainsKey(entry.Info.Id)) return;
        try
        {
            IModule module = entry.Create(context);
            module.ContentChanged += OnContentChanged;
            running[entry.Info.Id] = module;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(entry.Info.Name, ex.Message);
            return;
        }
        if (raise) Changed?.Invoke();
    }

    private void Stop(string id)
    {
        if (!running.Remove(id, out IModule? module)) return;
        module.ContentChanged -= OnContentChanged;
        module.Dispose();
        Changed?.Invoke();
    }

    private void OnContentChanged() => ContentChanged?.Invoke();

    public void Dispose()
    {
        foreach (IModule module in running.Values) module.Dispose();
        running.Clear();
    }
}

/// <summary>A row in Settings → Modules.</summary>
internal sealed class ModuleViewModel : ViewModelBase
{
    private readonly ModuleSettings settings;
    private readonly ModuleHost host;
    private Func<ModuleContext, IModule>? create;

    public ModuleViewModel(ModuleInfo info, ModuleSettings settings, ModuleHost host)
    {
        Info = info;
        create = info.Create;
        this.settings = settings;
        this.host = host;
    }

    public ModuleInfo Info { get; }
    public string Name => Info.Name;
    public string Glyph => Info.Glyph;
    public string Description => Info.Description;

    /// <summary>Set for plugins: install state, version and actions.</summary>
    public Plugins.PluginViewModel? Plugin { get; set; }

    internal Func<ModuleContext, IModule>? Create
    {
        get => create;
        set
        {
            create = value;
            Raise(nameof(IsAvailable));
            Raise(nameof(IsEnabled));
        }
    }

    /// <summary>Built in, or a plugin that is installed.</summary>
    public bool IsAvailable => create != null;

    /// <summary>What the user chose, whether or not the plugin is installed yet.</summary>
    public bool WantsEnabled
    {
        get => settings.Enabled;
        set => settings.Enabled = value;
    }

    public bool IsEnabled
    {
        get => settings.Enabled && IsAvailable;
        set
        {
            if (!IsAvailable || settings.Enabled == value) return;
            settings.Enabled = value;
            Raise();
            host.Apply(this);
        }
    }
}
