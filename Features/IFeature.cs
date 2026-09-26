using YsfUtil.Hotkeys;

namespace YsfUtil.Features;

/// <summary>
/// Eine Funktion von ysfUtil. Neue Funktion: Klasse anlegen und in
/// <see cref="FeatureCatalog.All"/> eintragen - Seite, Tray-Menü, Hotkey und Einstellungen
/// ergeben sich daraus von selbst.
/// </summary>
internal interface IFeature
{
    /// <summary>Stabiler Schlüssel für die Einstellungen, z. B. "taskbar-toggle".</summary>
    string Id { get; }

    string Name { get; }

    string Description { get; }

    /// <summary>Glyphe aus Segoe Fluent Icons für die Zeile.</summary>
    string Glyph { get; }

    Hotkey? DefaultHotkey { get; }

    /// <summary>Aktueller Zustand in wenigen Worten, oder null, wenn es keinen gibt.</summary>
    string? Status { get; }

    void Execute();
}

internal static class FeatureCatalog
{
    public static IReadOnlyList<IFeature> All { get; } =
    [
        new TaskbarToggleFeature(),
    ];
}
