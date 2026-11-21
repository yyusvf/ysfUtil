namespace YsfUtil.Features;

internal static class FeatureCatalog
{
    public static IReadOnlyList<IFeature> Create(Audio.AudioOutputSwitcher audio) =>
    [
        new TaskbarToggleFeature(),
        new DesktopIconsToggleFeature(),
        new DisplayModeToggleFeature(),
        new NightLightFixFeature(),
        new AudioOutputSwitchFeature(audio),
    ];
}
