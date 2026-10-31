namespace YsfUtil.Modules;

internal static class ModuleCatalog
{
    public static IReadOnlyList<ModuleInfo> All { get; } =
    [
        new("tools", "Tools",
            "Taskbar and desktop icon toggles, audio output switching, volume controls and the shutdown timer.",
            "\uE90F", context => new Tools.ToolsModule(context), EnabledByDefault: true),
        new("audio-mirror", "Audio Mirror",
            "Play Windows audio on several output devices at once.",
            "\uE7F5", context => new AudioMirror.AudioMirrorModule(context)),
        new("ds5-haptics", "DS5 BT Haptics",
            "DualSense HD haptics, adaptive triggers and light bar over Bluetooth.",
            "\uE7FC", null),
    ];
}
