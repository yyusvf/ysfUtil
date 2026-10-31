using System.Windows;
using YsfUtil.Ui.ViewModels;
using YsfUtil.Ui.Views;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace YsfUtil.Modules;

/// <summary>
/// A self-contained part of ysfUtil (the small tools, Audio Mirror, DS5 haptics, ...) that can be
/// switched on and off in Settings → Modules. A disabled module is never created, so it costs no
/// memory, threads or device handles. To add one, implement this and list it in
/// <see cref="ModuleCatalog"/>.
/// </summary>
internal interface IModule : IDisposable
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
internal enum ModuleCategory
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
internal sealed record ModuleCard(ModuleCategory Category, int Order, FrameworkElement View, Func<bool>? IsShown = null)
{
    public bool Shows => View.Visibility == Visibility.Visible && (IsShown?.Invoke() ?? true);
}

/// <summary>What the host offers a running module.</summary>
internal sealed class ModuleContext
{
    public required AppSettings Settings { get; init; }

    public required Func<bool> IsFlyoutVisible { get; init; }

    /// <summary>The passive, click-through overlay in the user's notification corner.</summary>
    public required Func<AudioSwitchBanner> Banner { get; init; }

    public required Action<string, string, ToolTipIcon> ShowHint { get; init; }

    /// <summary>Switches the flyout to Settings, then runs the callback once it is laid out.</summary>
    public required Action<Action?> ShowSettings { get; init; }

    /// <summary>Shows a file dialog over the flyout without closing it. The chosen path, or null.</summary>
    public required Func<Microsoft.Win32.OpenFileDialog, string?> PickFile { get; init; }

    /// <summary>Rendering a preview: never touch Windows (shutdown, saved microphone level, ...).</summary>
    public bool IsPreview { get; init; }
}

/// <summary>A catalog entry: everything Settings → Modules shows without creating the module.</summary>
internal sealed record ModuleInfo(
    string Id,
    string Name,
    string Description,
    string Glyph,
    Func<ModuleContext, IModule>? Create,
    bool EnabledByDefault = false)
{
    /// <summary>Listed but not built into this version yet.</summary>
    public bool IsAvailable => Create != null;
}
