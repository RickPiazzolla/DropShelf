using System.IO;
using Microsoft.Win32;

namespace DropShelf.Settings;

/// <summary>
/// Adds or removes DropShelf from the list of programs Windows starts at sign in.
/// </summary>
/// <remarks>
/// Written under HKEY_CURRENT_USER, which needs no elevation and affects only the
/// person who asked for it. The machine wide equivalent would require the app to
/// run as administrator, and an elevated shelf cannot accept drops at all, because
/// Windows blocks drags from a lower integrity level to a higher one.
/// </remarks>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DropShelf";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns the entry on or off, returning whether the change took effect.
    /// </summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return false;
            }

            // Quoted, because a path containing a space would otherwise be read as
            // a program name followed by arguments.
            key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}
