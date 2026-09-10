using System.Collections.ObjectModel;
using System.IO;

namespace DropShelf.Model;

/// <summary>
/// The collection of items currently being held.
/// </summary>
/// <remarks>
/// Paths are compared case insensitively, because Windows treats
/// <c>C:\Users\Report.pdf</c> and <c>c:\users\report.pdf</c> as the same file and
/// different applications hand over different casings for the same drop.
/// </remarks>
public sealed class Shelf
{
    private readonly ObservableCollection<ShelfItem> _items = [];

    public Shelf()
    {
        Items = new ReadOnlyObservableCollection<ShelfItem>(_items);
    }

    public ReadOnlyObservableCollection<ShelfItem> Items { get; }

    /// <summary>
    /// Adds every path that points at something real, ignoring the rest.
    /// </summary>
    /// <remarks>
    /// A dropped path list is not trustworthy input. It comes from another
    /// process, it can contain paths that no longer exist by the time the drop
    /// lands, and on a malformed drop it can contain strings that are not valid
    /// paths at all. Anything that does not resolve is skipped rather than
    /// reported, since a failed drop is not something the user asked about.
    /// </remarks>
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            var isDirectory = Directory.Exists(fullPath);
            if (!isDirectory && !File.Exists(fullPath))
            {
                continue;
            }

            AddOrPromote(fullPath, isDirectory);
        }
    }

    public void Remove(ShelfItem item) => _items.Remove(item);

    public void Clear() => _items.Clear();

    /// <summary>
    /// Returns the position of a path on the shelf, or -1 if it is not held.
    /// </summary>
    private int IndexOf(string fullPath)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Places a single verified path onto the shelf.
    /// </summary>
    /// <remarks>
    /// New items go to the front of the list, so the most recently dropped thing
    /// is the one nearest to hand.
    /// <para>
    /// A path already on the shelf is promoted rather than duplicated. Two tiles
    /// for one file would be indistinguishable, and the user would have no way to
    /// tell which of them they were about to drag out. Re-dropping something
    /// usually means the user lost track of it in a crowded shelf, and moving it
    /// back to the front answers that directly. The item keeps its original
    /// <see cref="ShelfItem.AddedAt"/>, since it never actually left.
    /// </para>
    /// </remarks>
    private void AddOrPromote(string fullPath, bool isDirectory)
    {
        var existingIndex = IndexOf(fullPath);

        if (existingIndex < 0)
        {
            _items.Insert(0, ShelfItem.FromPath(fullPath, isDirectory));
            return;
        }

        // Move on an item that is already at index 0 is a no-op that still raises
        // a collection changed event, which makes the tile flicker for no reason.
        if (existingIndex > 0)
        {
            _items.Move(existingIndex, 0);
        }
    }
}
