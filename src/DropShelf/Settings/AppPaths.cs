using System.IO;

namespace DropShelf.Settings;

/// <summary>
/// Where DropShelf keeps its own files.
/// </summary>
/// <remarks>
/// Local application data rather than roaming. The contents are paths to files on
/// this machine and a window position measured against this machine's monitors,
/// none of which mean anything on another computer.
/// </remarks>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DropShelf");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string ShelfFile => Path.Combine(Root, "shelf.json");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
