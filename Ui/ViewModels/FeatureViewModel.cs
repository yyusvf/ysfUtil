using YsfUtil.Features;
using YsfUtil.Hotkeys;

namespace YsfUtil.Ui.ViewModels;

/// <summary>
/// Eine Zeile der Funktionsliste. Änderungen gehen sofort in die Einstellungen; ob der
/// Hotkey sich anmelden ließ, trägt der <see cref="AppController"/> über <see cref="HotkeyError"/>
/// zurück.
/// </summary>
internal sealed class FeatureViewModel : ViewModelBase
{
    private readonly FeatureSettings settings;
    private string? status;
    private string? hotkeyError;

    public FeatureViewModel(IFeature feature, FeatureSettings settings)
    {
        Feature = feature;
        this.settings = settings;
        status = feature.Status;
        RunCommand = new RelayCommand(() => Run?.Invoke(this));
    }

    /// <summary>Etwas wurde geändert, das neu gespeichert und angemeldet werden muss.</summary>
    public event Action? Changed;

    /// <summary>Die Funktion soll ausgeführt werden.</summary>
    public event Action<FeatureViewModel>? Run;

    public IFeature Feature { get; }

    public string Name => Feature.Name;

    public string Description => Feature.Description;

    public string Glyph => Feature.Glyph;

    public RelayCommand RunCommand { get; }

    public string? Status
    {
        get => status;
        private set
        {
            if (Set(ref status, value))
            {
                Raise(nameof(StatusLine));
            }
        }
    }

    public bool IsEnabled
    {
        get => settings.Enabled;
        set
        {
            if (settings.Enabled == value)
            {
                return;
            }
            settings.Enabled = value;
            Raise();
            Changed?.Invoke();
        }
    }

    public Hotkey? Hotkey
    {
        get => Hotkeys.Hotkey.Parse(settings.Hotkey);
        set
        {
            string? text = value?.ToString();
            if (settings.Hotkey == text)
            {
                return;
            }
            settings.Hotkey = text;
            Raise();
            Changed?.Invoke();
        }
    }

    /// <summary>Warum der Hotkey nicht greift, oder null.</summary>
    public string? HotkeyError
    {
        get => hotkeyError;
        set
        {
            if (Set(ref hotkeyError, value))
            {
                Raise(nameof(HasHotkeyError));
                Raise(nameof(StatusLine));
            }
        }
    }

    public bool HasHotkeyError => hotkeyError != null;

    /// <summary>Zweite Zeile: der Fehler, wenn es einen gibt, sonst der Zustand.</summary>
    public string StatusLine => hotkeyError ?? status ?? Description;

    public void Refresh() => Status = Feature.Status;
}
