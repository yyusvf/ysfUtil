namespace YsfUtil.Audio;

internal sealed class AudioOutputSettings
{
    public bool SelectionConfigured { get; set; }
    public bool SwitchCommunications { get; set; } = true;
    public bool ShowBanner { get; set; } = true;
    public List<AudioOutputPreference> Devices { get; set; } = [];
}

internal sealed class AudioOutputPreference
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Included { get; set; } = true;
}
