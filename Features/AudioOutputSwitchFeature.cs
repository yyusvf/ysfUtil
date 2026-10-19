using YsfUtil.Audio;
using YsfUtil.Hotkeys;
using Keys = System.Windows.Forms.Keys;

namespace YsfUtil.Features;

internal sealed class AudioOutputSwitchFeature(AudioOutputSwitcher audio) : IFeature
{
    public string Id => "audio-output-switch";
    public string Name => "Audio output";
    public string Description => "Cycle through your selected playback devices.";
    public string Glyph => "\uE767";
    public Hotkey? DefaultHotkey => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.A);
    public string? Status => audio.Error ?? audio.Current?.Name ?? "No output device connected";
    public Task ExecuteAsync() => audio.CycleAsync();
}
