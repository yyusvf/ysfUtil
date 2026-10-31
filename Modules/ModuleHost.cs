using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Modules;

/// <summary>Settings of a single module.</summary>
internal sealed class ModuleSettings
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Creates the enabled modules and tears them down again when they are switched off. Modules are
/// started in catalog order, which is also the order of their cards in the flyout.
/// </summary>
internal sealed class ModuleHost : IDisposable
{
    private readonly ModuleContext context;
    private readonly Dictionary<string, IModule> running = new(StringComparer.OrdinalIgnoreCase);

    public ModuleHost(ModuleContext context)
    {
        this.context = context;
        Entries = ModuleCatalog.All.Select(info => new ModuleViewModel(info, context.Settings.For(info), this)).ToList();
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

    public IEnumerable<FeatureViewModel> Features => Running.SelectMany(m => m.Features);

    public bool HasVisibleContent => Running.Any(m => m.HasVisibleContent);

    public string? UpdateBlocker => Running.Select(m => m.UpdateBlocker).FirstOrDefault(b => b != null);

    public void StartEnabled()
    {
        foreach (ModuleViewModel entry in Entries.Where(e => e.IsEnabled))
        {
            Start(entry.Info, raise: false);
        }
        Changed?.Invoke();
    }

    internal void Apply(ModuleViewModel entry)
    {
        if (entry.IsEnabled) Start(entry.Info, raise: true);
        else Stop(entry.Info.Id);
        context.Settings.Save();
    }

    private void Start(ModuleInfo info, bool raise)
    {
        if (info.Create == null || running.ContainsKey(info.Id)) return;
        try
        {
            IModule module = info.Create(context);
            module.ContentChanged += OnContentChanged;
            running[info.Id] = module;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(info.Name, ex.Message);
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

    public ModuleViewModel(ModuleInfo info, ModuleSettings settings, ModuleHost host)
    {
        Info = info;
        this.settings = settings;
        this.host = host;
    }

    public ModuleInfo Info { get; }
    public string Name => Info.Name;
    public string Glyph => Info.Glyph;
    public bool IsAvailable => Info.IsAvailable;
    public string Description => Info.IsAvailable ? Info.Description : "Coming soon. " + Info.Description;

    public bool IsEnabled
    {
        get => settings.Enabled && Info.IsAvailable;
        set
        {
            if (!Info.IsAvailable || settings.Enabled == value) return;
            settings.Enabled = value;
            Raise();
            host.Apply(this);
        }
    }
}
