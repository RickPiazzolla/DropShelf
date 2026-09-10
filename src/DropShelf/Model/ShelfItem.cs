using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

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
public sealed class ShelfItem : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private bool _isSelected;

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
    /// The shell's picture for this file, or null until one has been produced.
    /// </summary>
    /// <remarks>
    /// Starts empty and is filled in later, because producing it means asking the
    /// shell, which is slow enough to be worth doing off the UI thread. The tile
    /// shows a placeholder in the meantime.
    /// </remarks>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether this item is part of the current selection.
    /// </summary>
    /// <remarks>
    /// Kept on the item rather than in a separate list on the shelf. A tile needs
    /// to show its own selected state, and binding straight to the item it already
    /// displays is far less to go wrong than keeping a parallel collection in step
    /// with one that items are constantly being added to and removed from.
    /// </remarks>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
