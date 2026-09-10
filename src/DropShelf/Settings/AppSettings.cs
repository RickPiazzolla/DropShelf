using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DropShelf.Settings;

/// <summary>
/// The handful of choices the user gets to make.
/// </summary>
/// <remarks>
/// Loading never throws. A settings file that has been hand edited into nonsense,
/// or truncated by a power cut mid write, should cost the user their preferences
/// and nothing else. Every failure path falls back to defaults.
/// </remarks>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Whether DropShelf launches when the user signs in.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Whether the shelf contents survive a restart.</summary>
    public bool RememberShelf { get; set; } = true;

    /// <summary>
    /// Whether an item leaves the shelf once it has been dragged somewhere.
    /// </summary>
    /// <remarks>
    /// Off by default. Leaving items in place lets the same file be dropped in
    /// several destinations, which is the safer behaviour to be surprised by. The
    /// people who want it want it because the shelf is a queue for them: drop
    /// things on, deal with them one at a time, and watch the pile go down.
    /// </remarks>
    public bool RemoveAfterDragOut { get; set; }

    /// <summary>Last known shelf position, null until it has been moved.</summary>
    public double? ShelfLeft { get; set; }

    public double? ShelfTop { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureRoot();
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Failing to record a preference is not worth interrupting anyone
            // over, and there is nothing useful the user could do about it.
        }
    }
}
