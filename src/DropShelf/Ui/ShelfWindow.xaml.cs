using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DropShelf.DropHandling;
using DropShelf.Model;
using DropShelf.Settings;

namespace DropShelf.Ui;

/// <summary>
/// The floating card that holds dragged items.
/// </summary>
/// <remarks>
/// The window is created once and then hidden and shown again, never closed.
/// Recreating it would lose its position, and more importantly would drop the
/// items it is holding. <see cref="OnClosing"/> enforces that: the only thing
/// allowed to close this window is application shutdown.
/// </remarks>
public partial class ShelfWindow : Window
{
    private readonly Shelf _shelf;
    private readonly DropReader _dropReader;
    private readonly AppSettings _settings;
    private bool _allowClose;
    private bool _positionRestored;

    /// <summary>
    /// True while an item is being dragged off the shelf.
    /// </summary>
    /// <remarks>
    /// The shelf usually sits against the right hand edge of the screen, which is
    /// exactly where the edge catcher watches for a drag. Without this the catcher
    /// slides in the instant the user starts dragging a tile away, right under
    /// their pointer, offering to catch the thing they are trying to remove.
    /// </remarks>
    public bool IsDraggingOut { get; private set; }

    public ShelfWindow(Shelf shelf, DropReader dropReader, AppSettings settings)
    {
        _shelf = shelf;
        _dropReader = dropReader;

        // The same instance the tray menu writes to, so a setting changed while
        // the shelf is open takes effect on the very next drag.
        _settings = settings;

        InitializeComponent();

        DataContext = _shelf;
    }

    /// <summary>
    /// Puts a replacement window where its predecessor was.
    /// </summary>
    /// <remarks>
    /// Used when the shelf has to be rebuilt to follow the user to another
    /// virtual desktop. Without this the shelf would jump back to its default
    /// corner every time, which would feel like a bug rather than a feature.
    /// </remarks>
    public void RestorePosition(double left, double top)
    {
        _positionRestored = true;
        Left = left;
        Top = top;
    }

    /// <summary>
    /// Parks the shelf against the right edge of the primary monitor's work area.
    /// </summary>
    private void MoveToDefaultPosition()
    {
        // WorkArea excludes the taskbar and is already in device independent
        // pixels, which is the same unit Left and Top expect. Using screen
        // pixels here would put the window in the wrong place on a scaled
        // display.
        var workArea = SystemParameters.WorkArea;

        const double EdgeGap = 8;
        Left = workArea.Right - ActualWidth - EdgeGap;
        Top = workArea.Top + ((workArea.Height - ActualHeight) / 2);
    }

    /// <summary>
    /// Shows the shelf without taking focus from whatever the user is doing.
    /// </summary>
    public void ShowShelf()
    {
        Show();

        // Positioned after Show rather than during window creation. The default
        // position is measured from the window's own width, and until the window
        // has been laid out that width is zero, which would park the shelf just
        // off the right hand edge of the screen.
        if (!_positionRestored)
        {
            _positionRestored = true;
            MoveToDefaultPosition();
        }

        // Topmost is set in XAML, but another topmost window shown later will sit
        // above this one. Re-asserting it on every show pushes the shelf back to
        // the front of that group.
        Topmost = false;
        Topmost = true;
    }

    public void HideShelf() => Hide();

    public void ToggleShelf()
    {
        if (IsVisible)
        {
            HideShelf();
        }
        else
        {
            ShowShelf();
        }
    }

    /// <summary>
    /// Closes the window for real. Everything else only hides it.
    /// </summary>
    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // DragMove throws if the button is no longer down by the time it runs,
        // which happens with a fast click, so the state is checked rather than
        // trusted.
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => HideShelf();

    private void OnClearClick(object sender, RoutedEventArgs e) => _shelf.Clear();

    private void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShelfItem item })
        {
            _shelf.Remove(item);
        }

        // Stops the click bubbling up to the tile behind the badge.
        e.Handled = true;
    }

    private void OpenItem(ShelfItem item)
    {
        if (!item.StillExists())
        {
            _shelf.Remove(item);
            return;
        }

        try
        {
            // UseShellExecute hands the path to the shell, which opens it with
            // whatever the user has associated with that type. Without it, .NET
            // tries to execute the file directly and anything that is not a
            // program fails.
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No association, or the user dismissed the "open with" dialog.
            // Neither is worth interrupting them over.
        }
    }

    private void OnCardDragOver(object sender, DragEventArgs e)
    {
        // Copy rather than Move. Move would tell the source application that
        // DropShelf has taken ownership, and Explorer acts on that by deleting
        // the original once the drop completes. The shelf only records a path.
        e.Effects = DropReader.CanRead(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        // Without this the parent chain gets a say and can override the effect,
        // which shows the user the wrong cursor.
        e.Handled = true;
    }

    private void OnCardDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        try
        {
            _shelf.AddPaths(_dropReader.Read(e.Data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            // Reading a drop means running other applications' data through
            // parsers and writing files. A failure here loses one drop, which is
            // recoverable by dropping again, and must not take the app down.
        }
    }

    // Dragging an item off the shelf.
    //
    // A press on a tile is ambiguous: it might become a drag, or it might just be
    // a click. Windows resolves this by distance, so the press is only recorded
    // here and the drag does not begin until the pointer has moved past the
    // system drag threshold. Starting on mouse down instead would make the shelf
    // impossible to click.
    private Point _pressOrigin;
    private ShelfItem? _pressedItem;
    private ShelfItem? _selectionAnchor;

    // Selection changes that would shrink the selection, held back until the
    // press is known to have been a click rather than the start of a drag.
    private ShelfItem? _collapseSelectionTo;
    private ShelfItem? _deselectOnRelease;

    private void OnItemMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The remove badge sits inside the tile, and preview events travel from
        // the outside in, so this handler sees the press before the button does.
        // Without this check a press that wanders a few pixels would start
        // dragging the file the user was trying to throw away.
        if (IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: ShelfItem item })
        {
            return;
        }

        // Border is a Decorator rather than a Control, so it has no
        // MouseDoubleClick event of its own. The click count on the press carries
        // the same information.
        if (e.ClickCount == 2)
        {
            _pressedItem = null;
            _collapseSelectionTo = null;
            OpenItem(item);
            e.Handled = true;
            return;
        }

        ApplySelectionForPress(item);

        _pressOrigin = e.GetPosition(null);
        _pressedItem = item;
    }

    /// <summary>
    /// Works out what a press should do to the selection.
    /// </summary>
    /// <remarks>
    /// Follows the conventions Explorer already taught everyone. Ctrl adds and
    /// removes one item, Shift extends from the last one touched, and a plain
    /// click selects just the one.
    /// <para>
    /// One rule governs the whole thing: a press must never make the selection
    /// smaller. A press is ambiguous until the pointer either moves or does not,
    /// and every press is a potential drag. Shrinking on the way down means the
    /// press that begins a drag first throws away part of what was about to be
    /// dragged, which is how a group of three arrives as two.
    /// </para>
    /// <para>
    /// So anything that removes items is recorded and applied on release, and only
    /// if no drag started. Anything that adds is safe to do immediately, since a
    /// drag then carries more rather than less.
    /// </para>
    /// </remarks>
    private void ApplySelectionForPress(ShelfItem item)
    {
        _collapseSelectionTo = null;
        _deselectOnRelease = null;

        var modifiers = Keyboard.Modifiers;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            _selectionAnchor = item;

            if (item.IsSelected)
            {
                // Deferred. Users habitually keep Ctrl held down after building a
                // selection, and toggling here would drop this item from the very
                // drag it is starting.
                _deselectOnRelease = item;
            }
            else
            {
                _shelf.AddToSelection(item);
            }

            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift) && _selectionAnchor is not null)
        {
            _shelf.SelectRange(_selectionAnchor, item);
            return;
        }

        if (item.IsSelected && _shelf.SelectedCount > 1)
        {
            // Also deferred, for the same reason. Narrowing to this one item now
            // would make dragging a group impossible.
            _collapseSelectionTo = item;
            _selectionAnchor = item;
            return;
        }

        _shelf.SelectOnly(item);
        _selectionAnchor = item;
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnItemMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pressedItem = null;

        // The press was a click after all, not the beginning of a drag, so the
        // changes that would have made the selection smaller can happen now.
        if (_collapseSelectionTo is { } narrowTo)
        {
            _collapseSelectionTo = null;
            _shelf.SelectOnly(narrowTo);
        }

        if (_deselectOnRelease is { } deselect)
        {
            _deselectOnRelease = null;
            _shelf.RemoveFromSelection(deselect);
        }
    }

    /// <summary>
    /// Clears the selection when the user clicks the background rather than a tile.
    /// </summary>
    private void OnPanelMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only when the click landed on the panel itself. A click that started on
        // a tile bubbles up to here too, and clearing then would undo the
        // selection the tile just made.
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            _shelf.ClearSelection();
            _selectionAnchor = null;
        }
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(null) - _pressOrigin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _pressedItem;

        // Cleared before the drag rather than after. DoDragDrop runs its own
        // message loop and does not return until the user lets go, so anything
        // after the call happens much later than it reads.
        _pressedItem = null;

        // The press turned into a drag, so nothing that would shrink the selection
        // is allowed to happen. Otherwise dragging a group of four quietly turns
        // into dragging one, or three.
        _collapseSelectionTo = null;
        _deselectOnRelease = null;

        BeginItemDrag(sender as DependencyObject, item);
    }

    /// <summary>
    /// Drags the selection, or just the item under the pointer if nothing is
    /// selected.
    /// </summary>
    private void BeginItemDrag(DependencyObject? source, ShelfItem item)
    {
        if (source is null)
        {
            return;
        }

        var dragging = _shelf.SelectionOrJust(item);

        // The user is free to delete or move a file while it sits on the shelf.
        // Handing a dead path to another application produces a confusing error
        // in that application rather than in this one, so they are checked first
        // and anything that has gone away is quietly taken off the shelf.
        var missing = dragging.Where(candidate => !candidate.StillExists()).ToList();
        if (missing.Count > 0)
        {
            _shelf.RemoveAll(missing);
            dragging = dragging.Except(missing).ToList();
        }

        if (dragging.Count == 0)
        {
            return;
        }

        var paths = dragging.Select(candidate => candidate.FullPath).ToArray();

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);

        // Some older targets, and most plain text fields, take the paths as text.
        // One per line is what Explorer produces for a multiple file copy.
        data.SetData(DataFormats.UnicodeText, string.Join(Environment.NewLine, paths));

        // Marks the drag as ours so that neither the shelf nor the edge catcher
        // will take it back in. See DropReader.SelfDragFormat for why that
        // matters, and SelfDragValue for why it is a string.
        DropReader.MarkAsOwnDrag(data);

        DragDropEffects result;
        IsDraggingOut = true;

        try
        {
            // Move is deliberately not offered. With a file drag the target
            // carries out the operation, and a target that chose Move would
            // delete the user's original file. The shelf holds a reference, not a
            // copy, so it has no business authorising that.
            result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Link);
        }
        catch (COMException)
        {
            // The shell refuses to start a drag while another one is already in
            // progress, which happens if the user is quick. There is nothing to
            // recover and nothing the user needs told.
            return;
        }
        finally
        {
            IsDraggingOut = false;
        }

        // None means the drag was abandoned, over an app that would not take it or
        // by pressing Escape. Clearing the shelf on that would lose the item for
        // nothing, so the setting only applies to a drop that actually landed.
        if (_settings.RemoveAfterDragOut && result != DragDropEffects.None)
        {
            _shelf.RemoveAll(dragging);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // Alt+F4 and the like would otherwise destroy the window and take the
            // shelf contents with it.
            e.Cancel = true;
            HideShelf();
            return;
        }

        base.OnClosing(e);
    }
}
