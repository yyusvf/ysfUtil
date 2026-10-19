using YsfUtil.Hotkeys;

namespace YsfUtil.Features;

/// <summary>
/// A ysfUtil feature. To add one, implement this and list it in <see cref="FeatureCatalog.Create"/> -
/// flyout card, hotkey and settings follow automatically.
/// </summary>
internal interface IFeature
{
    /// <summary>Stable settings key, e.g. "taskbar-toggle".</summary>
    string Id { get; }

    string Name { get; }

    string Description { get; }

    /// <summary>Segoe Fluent Icons glyph for the card.</summary>
    string Glyph { get; }

    Hotkey? DefaultHotkey { get; }

    /// <summary>Current state in a few words, or null if there is none.</summary>
    string? Status { get; }

    Task ExecuteAsync();
}

internal static class FeatureCatalog
{
    public static IReadOnlyList<IFeature> Create(Audio.AudioOutputSwitcher audio) =>
    [
        new TaskbarToggleFeature(),
        new DesktopIconsToggleFeature(),
        new AudioOutputSwitchFeature(audio),
    ];
}
