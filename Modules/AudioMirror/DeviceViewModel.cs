using System.Collections.ObjectModel;
using YsfUtil.Modules.AudioMirror.Engine;
using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Modules.AudioMirror;

/// <summary>An application in the mix of one output device.</summary>
internal sealed class AppViewModel : ViewModelBase
{
    private bool isEnabled = true;
    private float volume = 1f;
    private string status = string.Empty;

    public AppViewModel(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public bool IsEnabled
    {
        get => isEnabled;
        set => Set(ref isEnabled, value);
    }

    /// <summary>0..1 for the engine.</summary>
    public float Volume
    {
        get => volume;
        set
        {
            if (Set(ref volume, value))
            {
                Raise(nameof(Percent));
                Raise(nameof(VolumeText));
            }
        }
    }

    /// <summary>0..100 for the slider.</summary>
    public double Percent
    {
        get => Math.Round(volume * 100);
        set => Volume = (float)Math.Clamp(value / 100, 0, 1);
    }

    public string VolumeText => $"{volume * 100:0}%";

    /// <summary>A hint on the row, e.g. when the app's sound cannot be captured.</summary>
    public string Status
    {
        get => status;
        set
        {
            if (Set(ref status, value))
            {
                Raise(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(status);
}

/// <summary>An output device in the list. What becomes of it is decided by <see cref="AudioMirrorModule"/>.</summary>
internal sealed class DeviceViewModel : ViewModelBase
{
    private bool isEnabled;
    private float volume = 1f;
    private string status = string.Empty;

    public DeviceViewModel(string deviceId, string displayName, AudioDeviceKind kind, bool isSource,
        string? iconPath = null)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
        Kind = kind;
        IsSource = isSource;
        Icon = DeviceIconSource.TryLoad(iconPath);
        Apps.CollectionChanged += (_, _) => Raise(nameof(HasApps));
    }

    public string DeviceId { get; }

    public string DisplayName { get; }

    public AudioDeviceKind Kind { get; }

    /// <summary>The source already plays the sound itself; as a target it would feed back.</summary>
    public bool IsSource { get; }

    public bool CanToggle => !IsSource;

    /// <summary>The icon Windows assigns to the device, or null - then the row shows <see cref="Glyph"/>.</summary>
    public System.Windows.Media.ImageSource? Icon { get; }

    public bool HasIcon => Icon != null;

    public string Glyph => Kind switch
    {
        AudioDeviceKind.Headphones => "",
        AudioDeviceKind.Display => "",
        AudioDeviceKind.Digital => "",
        _ => "",
    };

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (Set(ref isEnabled, value)) Raise(nameof(ShowsVolume));
        }
    }

    public bool ShowsVolume => isEnabled && !IsSource;

    /// <summary>0..1 for the engine; multiplies with each app's volume.</summary>
    public float Volume
    {
        get => volume;
        set
        {
            if (Set(ref volume, value))
            {
                Raise(nameof(Percent));
                Raise(nameof(VolumeText));
            }
        }
    }

    /// <summary>0..100 for the slider.</summary>
    public double Percent
    {
        get => Math.Round(volume * 100);
        set => Volume = (float)Math.Clamp(value / 100, 0, 1);
    }

    public string VolumeText => $"{volume * 100:0}%";

    public string Status
    {
        get => status;
        set
        {
            if (Set(ref status, value)) Raise(nameof(StatusLine));
        }
    }

    /// <summary>Second line: what the device is doing, or that it's the source.</summary>
    public string StatusLine => IsSource ? Strings.SourceShort : status;

    public ObservableCollection<AppViewModel> Apps { get; } = [];

    public bool HasApps => Apps.Count > 0;
}
