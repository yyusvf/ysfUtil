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

/// <summary>Einstellungen einer einzelnen Funktion.</summary>
internal sealed class FeatureSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Als Text wie "Ctrl+Alt+T" - so bleibt die Datei von Hand lesbar. Leer heißt: kein Hotkey.</summary>
    public string? Hotkey { get; set; }
}

/// <summary>
/// Alles, was ysfUtil sich merkt, in %APPDATA%\ysfUtil\settings.json.
///
/// Gespeichert wird sofort bei jeder Änderung - es gibt keinen Speichern-Knopf, wie in den
/// Windows-Einstellungen auch.
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

    /// <summary>Nach Kennung der Funktion, siehe <see cref="Features.IFeature.Id"/>.</summary>
    public Dictionary<string, FeatureSettings> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gab es noch keine Datei? Dann läuft ysfUtil zum ersten Mal.</summary>
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
                    // Der Vergleich ohne Groß- und Kleinschreibung geht beim Einlesen verloren.
                    loaded.Features = new(loaded.Features, StringComparer.OrdinalIgnoreCase);
                    return loaded;
                }
            }
        }
        catch
        {
            // Kaputte Datei - lieber mit Standardwerten starten als gar nicht.
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
            // Nicht schreibbar - die Einstellung gilt dann bis zum Beenden.
        }
    }

    /// <summary>Die Einstellungen einer Funktion; beim ersten Zugriff mit ihrem Standard-Hotkey angelegt.</summary>
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
