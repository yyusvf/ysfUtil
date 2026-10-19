using System.Text.Json;

namespace YsfUtil.Audio;

/// <summary>Read-only import of playback selections. Never edits SoundSwitch's files.</summary>
internal static class SoundSwitchImport
{
    public static IReadOnlyList<AudioOutputPreference> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<AudioOutputPreference>();
        if (document.RootElement.TryGetProperty("SelectedDevices", out var devices)
            && devices.ValueKind == JsonValueKind.Array)
        {
            foreach (var device in devices.EnumerateArray())
            {
                if (!device.TryGetProperty("Type", out var type) || type.ValueKind != JsonValueKind.Number
                    || !type.TryGetInt32(out int kind) || kind != 0 || !device.TryGetProperty("Id", out var id)
                    || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())) continue;
                string key = id.GetString()!;
                if (result.Any(d => d.Id == key)) continue;
                result.Add(new AudioOutputPreference
                {
                    Id = key,
                    Name = device.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String
                        ? name.GetString()! : "Saved output",
                });
            }
        }
        if (result.Count == 0)
            throw new InvalidOperationException("No playback device selection was found in SoundSwitch.");
        return result;
    }
}
