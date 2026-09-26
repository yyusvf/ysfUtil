using Microsoft.Win32;

namespace YsfUtil;

/// <summary>
/// "Mit Windows starten" über den Run-Schlüssel des Benutzers. Der Eintrag trägt
/// <see cref="QuietArgument"/>, damit ysfUtil dann nur im Infobereich startet.
/// </summary>
internal static class Autostart
{
    public const string QuietArgument = "--tray";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ysfUtil";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {QuietArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Zieht einen bestehenden Eintrag auf den aktuellen Pfad nach - etwa nachdem die exe
    /// verschoben wurde. Sonst zeigt der Autostart ins Leere.
    /// </summary>
    public static void Refresh()
    {
        if (IsEnabled())
        {
            try
            {
                SetEnabled(true);
            }
            catch
            {
                // Unkritisch.
            }
        }
    }
}
