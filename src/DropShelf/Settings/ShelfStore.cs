using System.IO;
using System.Text.Json;

namespace DropShelf.Settings;

/// <summary>
/// Remembers what was on the shelf between runs.
/// </summary>
/// <remarks>
/// Only the paths are stored. The shelf never held copies of anything, so there is
/// nothing else to keep, and a restored shelf is exactly as valid as the files it
/// points at still being where they were.
/// </remarks>
internal static class ShelfStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ShelfFile))
            {
                return [];
            }

            var json = File.ReadAllText(AppPaths.ShelfFile);
            return JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<string> paths)
    {
        try
        {
            AppPaths.EnsureRoot();
            File.WriteAllText(AppPaths.ShelfFile, JsonSerializer.Serialize(paths.ToList(), Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Losing the remembered shelf is a small disappointment at next
            // start-up, not something to fail on now.
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(AppPaths.ShelfFile))
            {
                File.Delete(AppPaths.ShelfFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to do about it.
        }
    }
}
