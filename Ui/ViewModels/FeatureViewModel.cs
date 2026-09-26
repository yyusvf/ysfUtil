using YsfUtil.Features;
using YsfUtil.Hotkeys;

namespace YsfUtil.Ui.ViewModels;

/// <summary>
/// A feature card. Changes go straight into the settings; whether the hotkey could be
/// registered is reported back by <see cref="AppController"/> via <see cref="HotkeyError"/>.
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

    /// <summary>Something changed that needs saving and re-registering.</summary>
    public event Action? Changed;

    /// <summary>The feature should run.</summary>
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

    /// <summary>Why the hotkey does not work, or null.</summary>
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

    /// <summary>Second line: the error if any, otherwise the status.</summary>
    public string StatusLine => hotkeyError ?? status ?? Description;

    public void Refresh() => Status = Feature.Status;
}
