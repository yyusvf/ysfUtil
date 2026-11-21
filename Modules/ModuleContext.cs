using System.Diagnostics;
using YsfUtil.Features;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using YsfUtil.Ui.Views;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace YsfUtil.Modules;

/// <summary>What the host offers its modules. Built-in modules use it directly.</summary>
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

/// <summary>A plugin's view of <see cref="ModuleContext"/>: its own settings file and processes.</summary>
internal sealed class PluginContext(ModuleContext host, Plugins.InstalledPlugin plugin) : IModuleContext
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string SettingsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ysfUtil", "plugins");

    private string SettingsPath => Path.Combine(SettingsFolder, plugin.Manifest.Id + ".json");

    public string ModuleId => plugin.Manifest.Id;
    public bool IsFlyoutVisible => host.IsFlyoutVisible();
    public bool IsPreview => host.IsPreview;
    public void ShowHint(string title, string text, ToolTipIcon icon) => host.ShowHint(title, text, icon);
    public void ShowSettings(Action? then = null) => host.ShowSettings(then);
    public string? PickFile(Microsoft.Win32.OpenFileDialog dialog) => host.PickFile(dialog);
    public FeatureSettings FeatureSettings(IFeature feature) => host.Settings.For(feature);

    public T LoadSettings<T>(Func<T> createDefault) where T : class
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(SettingsPath), Json) ?? createDefault();
            // Before plugins, built-in modules kept their settings in settings.json.
            if (Plugins.PluginCatalog.Find(ModuleId)?.LegacySettingsKey is { } key
                && host.Settings.Legacy?.TryGetValue(key, out JsonElement legacy) == true)
                return legacy.Deserialize<T>(Json) ?? createDefault();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Broken file - better to start with defaults than not at all.
        }
        return createDefault();
    }

    public void SaveSettings<T>(T settings) where T : class
    {
        if (IsPreview) return;
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not writable - the setting then only lasts until exit.
        }
    }

    public ProcessStartInfo CreateProcess(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in Plugins.PluginProcess.Arguments(plugin).Concat(arguments))
            info.ArgumentList.Add(argument);
        return info;
    }
}
