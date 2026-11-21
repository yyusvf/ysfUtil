namespace YsfUtil.Modules;

/// <summary>Modules built into ysfUtil. Everything else comes as a plugin, see Plugins.PluginCatalog.</summary>
internal static class ModuleCatalog
{
    public static IReadOnlyList<ModuleInfo> All { get; } =
    [
        new("tools", "Tools",
            "Taskbar, desktop icon and display toggles, a night light fix, audio output switching, volume controls and the shutdown timer.",
            "\uE90F", context => new Tools.ToolsModule(context), EnabledByDefault: true),
    ];
}
