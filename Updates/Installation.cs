using System.IO;

namespace YsfUtil.Updates;

internal static class Installation
{
    internal const string Marker = "ysfUtil.installation";
    public static bool IsInstalled => IsInstalledDirectory(AppContext.BaseDirectory);
    internal static bool IsInstalledDirectory(string directory)
    {
        try
        {
            return File.Exists(Path.Combine(directory, "unins000.exe"))
                && File.ReadAllText(Path.Combine(directory, Marker)).Trim() == "ysfUtil.Setup";
        }
        catch { return false; }
    }
}
