using System.Windows;
using YsfUtil.Features;
using YsfUtil.Ui.ViewModels;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace YsfUtil.Modules;

/// <summary>
/// A self-contained part of ysfUtil (the small tools, Audio Mirror, DS5 haptics, ...) that can be
/// switched on and off in Settings → Modules. A disabled module is never created, so it costs no
/// memory, threads or device handles. A plugin names its module type in its manifest; ysfUtil
/// creates it with a constructor taking <see cref="IModuleContext"/>.
/// </summary>
public interface IModule : IDisposable
{
    /// <summary>Actions with a hotkey. The host registers them and reports conflicts across modules.</summary>
    IReadOnlyList<FeatureViewModel> Features { get; }

    /// <summary>Cards for the flyout's main view, filed under categories. Should stay compact - the tray is shared.</summary>
    IReadOnlyList<ModuleCard> FlyoutCards { get; }

    /// <summary>Cards for the flyout's Settings view, filed under the same categories.</summary>
    IReadOnlyList<ModuleCard> SettingsCards { get; }

    /// <summary>Whether <see cref="FlyoutCards"/> currently show anything; drives the empty state.</summary>
    bool HasVisibleContent { get; }

    /// <summary><see cref="HasVisibleContent"/> changed.</summary>
    event Action? ContentChanged;

    /// <summary>Why ysfUtil must not restart for an update right now, or null.</summary>
    string? UpdateBlocker { get; }

    /// <summary>The flyout is about to show: re-read state that may have changed elsewhere.</summary>
    void OnFlyoutOpening();
}

/// <summary>
/// Where a card goes. Cards of every module are grouped by this, so Audio Mirror sits next to the
/// audio output and volume cards rather than below everything else. Sections appear in this order.
/// </summary>
public enum ModuleCategory
{
    /// <summary>Settings only: right below Settings → Modules, without a heading of its own.</summary>
    Modules,
    Audio,
    Desktop,
    System,
    Controller,
}

/// <summary>
/// A card of a module. <paramref name="Order"/> sorts cards within their category across modules.
/// <paramref name="IsShown"/> says whether the card shows anything beyond its own Visibility (a
/// list whose items all hide themselves); the section heading hides when no card is shown.
/// </summary>
public sealed record ModuleCard(ModuleCategory Category, int Order, FrameworkElement View, Func<bool>? IsShown = null)
{
    public bool Shows => View.Visibility == Visibility.Visible && (IsShown?.Invoke() ?? true);
}

/// <summary>What ysfUtil offers a running module. Each module gets its own.</summary>
public interface IModuleContext
{
    /// <summary>The module's id, as in the plugin manifest.</summary>
    string ModuleId { get; }

    bool IsFlyoutVisible { get; }

    /// <summary>Rendering a preview: never touch Windows, start processes or save anything.</summary>
    bool IsPreview { get; }

    /// <summary>A balloon from the tray icon.</summary>
    void ShowHint(string title, string text, ToolTipIcon icon);

    /// <summary>Switches the flyout to Settings, then runs the callback once it is laid out.</summary>
    void ShowSettings(Action? then = null);

    /// <summary>Shows a file dialog over the flyout without closing it. The chosen path, or null.</summary>
    string? PickFile(Microsoft.Win32.OpenFileDialog dialog);

    /// <summary>Enabled state and hotkey of a feature, kept in ysfUtil's settings.</summary>
    FeatureSettings FeatureSettings(IFeature feature);

    /// <summary>The module's own settings (JSON, camelCase), or <paramref name="createDefault"/>'s result the first time.</summary>
    T LoadSettings<T>(Func<T> createDefault) where T : class;

    void SaveSettings<T>(T settings) where T : class;

    /// <summary>
    /// Starts ysfUtil.exe so that it runs the plugin's <see cref="IPluginProcess"/> with
    /// <paramref name="arguments"/> - for work that must not share the tray's process, or needs
    /// administrator rights (set Verb = "runas" and UseShellExecute = true).
    /// </summary>
    System.Diagnostics.ProcessStartInfo CreateProcess(IEnumerable<string> arguments);
}

/// <summary>Entry point for <see cref="IModuleContext.CreateProcess"/>, named in the plugin manifest.</summary>
public interface IPluginProcess
{
    /// <summary>Runs on the process's main thread; the return value is the exit code.</summary>
    int Run(string[] arguments);
}

/// <summary>The plugin interface version. A plugin's manifest names the version it was built for.</summary>
public static class PluginApi
{
    /// <summary>Raised only for breaking changes; ysfUtil loads plugins built for this version.</summary>
    public const int Version = 1;

    /// <summary>The manifest's file name inside a plugin folder.</summary>
    public const string ManifestFile = "plugin.json";
}
