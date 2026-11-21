namespace YsfUtil;

/// <summary>Settings of a single feature.</summary>
public sealed class FeatureSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Stored as text like "Ctrl+Alt+T" so the file stays readable. Empty means no hotkey.</summary>
    public string? Hotkey { get; set; }
}
