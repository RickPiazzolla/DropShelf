using System.IO;

namespace DropShelf.Model;

/// <summary>
/// One thing the shelf is holding.
/// </summary>
/// <remarks>
/// The shelf stores a path, not a copy of the file. Nothing is duplicated on
/// disk, so putting a 4GB video on the shelf costs nothing. The trade-off is that
/// an item can go stale: the user is free to delete or move the file while it is
/// sitting here, and the path will then point at nothing.
/// </remarks>
public sealed class ShelfItem
{
    private ShelfItem(string fullPath, string displayName, bool isDirectory)
    {
        FullPath = fullPath;
        DisplayName = displayName;
        IsDirectory = isDirectory;
        AddedAt = DateTimeOffset.Now;
    }

    /// <summary>The absolute path this item refers to.</summary>
    public string FullPath { get; }

    /// <summary>The file or folder name, which is what the tile shows.</summary>
    public string DisplayName { get; }

    public bool IsDirectory { get; }

    public DateTimeOffset AddedAt { get; }

    /// <summary>
    /// True if the path still resolves to something on disk.
    /// </summary>
    /// <remarks>
    /// Deliberately a method rather than a property. It hits the file system every
    /// time it is called, and a property implies something cheaper than that.
    /// </remarks>
    public bool StillExists() => IsDirectory ? Directory.Exists(FullPath) : File.Exists(FullPath);

    public static ShelfItem FromPath(string fullPath, bool isDirectory)
    {
        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(name))
        {
            // A drive root such as "D:\" has no file name component, and a tile
            // labelled with an empty string would be a mystery.
            name = fullPath;
        }

        return new ShelfItem(fullPath, name, isDirectory);
    }
}
