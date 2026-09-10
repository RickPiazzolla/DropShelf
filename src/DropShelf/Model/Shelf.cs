using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using DropShelf.Interop;

namespace DropShelf.Model;

/// <summary>
/// The collection of items currently being held.
/// </summary>
/// <remarks>
/// Paths are compared case insensitively, because Windows treats
/// <c>C:\Users\Report.pdf</c> and <c>c:\users\report.pdf</c> as the same file and
/// different applications hand over different casings for the same drop.
/// </remarks>
public sealed class Shelf : INotifyPropertyChanged
{
    /// <summary>
    /// Pixel size requested from the shell for tile images.
    /// </summary>
    /// <remarks>
    /// Larger than the 40 unit box a tile draws it in, so the picture still looks
    /// sharp on a display running at 200 per cent scaling. Asking for exactly the
    /// display size would look soft on every laptop sold in the last decade.
    /// </remarks>
    private const int ThumbnailPixelSize = 96;

    private readonly ObservableCollection<ShelfItem> _items = [];
    private readonly ThumbnailLoader? _thumbnails;

    public Shelf(ThumbnailLoader? thumbnails = null)
    {
        _thumbnails = thumbnails;
        Items = new ReadOnlyObservableCollection<ShelfItem>(_items);

        // Removing a selected item changes the count without any item's own
        // IsSelected ever being set, so the collection has to be watched too.
        _items.CollectionChanged += OnItemsChanged;
    }

    public ReadOnlyObservableCollection<ShelfItem> Items { get; }

    /// <summary>
    /// How many items are currently selected.
    /// </summary>
    public int SelectedCount => _items.Count(item => item.IsSelected);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The selected items in shelf order, or the single item passed in if nothing
    /// is selected at all.
    /// </summary>
    /// <remarks>
    /// Shelf order rather than the order they were clicked in. Dragging four files
    /// into a folder should not depend on which one the user happened to click
    /// first, and the order they see on screen is the only one they can predict.
    /// </remarks>
    public IReadOnlyList<ShelfItem> SelectionOrJust(ShelfItem item)
    {
        var selected = _items.Where(i => i.IsSelected).ToList();
        return selected.Count > 0 ? selected : [item];
    }

    /// <summary>
    /// Makes this the only selected item.
    /// </summary>
    public void SelectOnly(ShelfItem item)
    {
        foreach (var candidate in _items)
        {
            candidate.IsSelected = ReferenceEquals(candidate, item);
        }

        RaiseSelectionChanged();
    }

    /// <summary>
    /// Adds or removes a single item from the selection, leaving the rest alone.
    /// </summary>
    public void ToggleSelection(ShelfItem item)
    {
        item.IsSelected = !item.IsSelected;
        RaiseSelectionChanged();
    }

    /// <summary>
    /// Adds one item to the selection, leaving everything else as it is.
    /// </summary>
    public void AddToSelection(ShelfItem item)
    {
        if (item.IsSelected)
        {
            return;
        }

        item.IsSelected = true;
        RaiseSelectionChanged();
    }

    /// <summary>
    /// Takes one item out of the selection, leaving everything else as it is.
    /// </summary>
    public void RemoveFromSelection(ShelfItem item)
    {
        if (!item.IsSelected)
        {
            return;
        }

        item.IsSelected = false;
        RaiseSelectionChanged();
    }

    /// <summary>
    /// Selects everything between two items inclusive, keeping what was already
    /// selected.
    /// </summary>
    public void SelectRange(ShelfItem from, ShelfItem to)
    {
        var first = _items.IndexOf(from);
        var last = _items.IndexOf(to);

        if (first < 0 || last < 0)
        {
            // One of them has been removed since. Fall back to the one still here.
            SelectOnly(_items.Contains(to) ? to : from);
            return;
        }

        if (first > last)
        {
            (first, last) = (last, first);
        }

        for (var i = first; i <= last; i++)
        {
            _items[i].IsSelected = true;
        }

        RaiseSelectionChanged();
    }

    public void SelectAll()
    {
        foreach (var item in _items)
        {
            item.IsSelected = true;
        }

        RaiseSelectionChanged();
    }

    public void ClearSelection()
    {
        foreach (var item in _items)
        {
            item.IsSelected = false;
        }

        RaiseSelectionChanged();
    }

    public void RemoveAll(IEnumerable<ShelfItem> items)
    {
        // Copied first. Removing from the collection while enumerating a query
        // over that same collection would throw partway through.
        foreach (var item in items.ToList())
        {
            _items.Remove(item);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseSelectionChanged();

    private void RaiseSelectionChanged() => OnPropertyChanged(nameof(SelectedCount));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
            var item = ShelfItem.FromPath(fullPath, isDirectory);
            _items.Insert(0, item);

            // Fire and forget. The tile appears straight away with a placeholder
            // and swaps in the real picture whenever the shell gets round to it.
            _thumbnails?.Request(fullPath, ThumbnailPixelSize, image => item.Thumbnail = image);
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
