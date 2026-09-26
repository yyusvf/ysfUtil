using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YsfUtil;

internal enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Settings of a single feature.</summary>
internal sealed class FeatureSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Stored as text like "Ctrl+Alt+T" so the file stays readable. Empty means no hotkey.</summary>
    public string? Hotkey { get; set; }
}

/// <summary>
/// Everything ysfUtil remembers, in %APPDATA%\ysfUtil\settings.json. Saved immediately on every
/// change - there is no save button.
/// </summary>
internal sealed class AppSettings
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ysfUtil");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>Keyed by feature id, see <see cref="Features.IFeature.Id"/>.</summary>
    public Dictionary<string, FeatureSettings> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>No settings file yet, so this is the first run.</summary>
    [JsonIgnore]
    public bool IsFirstRun { get; private set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (loaded != null)
                {
                    // The case-insensitive comparer is lost during deserialization.
                    loaded.Features = new(loaded.Features, StringComparer.OrdinalIgnoreCase);
                    return loaded;
                }
            }
        }
        catch
        {
            // Broken file - better to start with defaults than not at all.
        }

        return new AppSettings { IsFirstRun = !File.Exists(FilePath) };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Not writable - the setting then only lasts until exit.
        }
    }

    /// <summary>A feature's settings, created with its default hotkey on first access.</summary>
    public FeatureSettings For(Features.IFeature feature)
    {
        if (!Features.TryGetValue(feature.Id, out FeatureSettings? settings))
        {
            settings = new FeatureSettings { Hotkey = feature.DefaultHotkey?.ToString() };
            Features[feature.Id] = settings;
        }
        return settings;
    }
}
