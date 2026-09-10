using System.IO;

namespace DropShelf.DropHandling;

/// <summary>
/// The folder where dropped content that was not already a file gets written.
/// </summary>
/// <remarks>
/// Dragging a picture out of a web page hands over pixels, not a path. Dragging an
/// attachment out of an email client hands over a promise that the file can be
/// produced on request. Neither exists on disk, and the shelf holds paths, so
/// something has to write them somewhere first.
/// <para>
/// Nothing here is ever deleted automatically. A staged file may be the only copy
/// of an attachment the user pulled out of an email, and quietly removing it later
/// would destroy data they thought they had saved. The folder is reachable from
/// the tray menu so it can be cleared out deliberately.
/// </para>
/// </remarks>
public sealed class StagingArea
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    public StagingArea()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DropShelf",
            "Staged");
    }

    /// <summary>The root folder. May not exist until something is staged.</summary>
    public string Root { get; }

    /// <summary>
    /// Creates a folder for one drop.
    /// </summary>
    /// <remarks>
    /// Each drop gets its own folder so that two files with the same name from
    /// two different emails do not collide, and so the user can see which files
    /// arrived together.
    /// </remarks>
    public string CreateBatchFolder()
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var folder = Path.Combine(Root, stamp);

        // Two drops inside the same second are rare but perfectly possible.
        var attempt = 2;
        while (Directory.Exists(folder))
        {
            folder = Path.Combine(Root, $"{stamp} ({attempt})");
            attempt++;
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Turns a name supplied by another application into one that is safe to
    /// write inside <paramref name="folder"/>.
    /// </summary>
    /// <remarks>
    /// The name arrives from another process and cannot be trusted. Taking only
    /// the file name component defeats a value like <c>..\..\startup\evil.exe</c>,
    /// which would otherwise write outside the staging area entirely.
    /// </remarks>
    public static string SafePathFor(string folder, string suggestedName)
    {
        var name = Path.GetFileName(suggestedName ?? string.Empty);

        foreach (var invalid in InvalidNameChars)
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim();
        if (name.Length == 0 || name is "." or "..")
        {
            name = "Dropped item";
        }

        // Windows refuses these regardless of extension, as a leftover from DOS
        // device names.
        var stem = Path.GetFileNameWithoutExtension(name);
        if (IsReservedDeviceName(stem))
        {
            name = "_" + name;
        }

        var candidate = Path.Combine(folder, name);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var i = 2; i < 1000; i++)
        {
            candidate = Path.Combine(folder, $"{baseName} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{baseName} ({Guid.NewGuid():N}){extension}");
    }

    private static bool IsReservedDeviceName(string stem)
    {
        string[] reserved =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ];

        return Array.Exists(reserved, r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase));
    }
}
