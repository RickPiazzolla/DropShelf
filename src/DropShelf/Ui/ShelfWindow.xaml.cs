using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DropShelf.DropHandling;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DropShelf.Model;

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
    private bool _allowClose;
    private bool _positionRestored;

    public ShelfWindow(Shelf shelf, DropReader dropReader)
    {
        _shelf = shelf;
        _dropReader = dropReader;

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
            OpenItem(item);
            e.Handled = true;
            return;
        }

        _pressOrigin = e.GetPosition(null);
        _pressedItem = item;
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

    private void OnItemMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pressedItem = null;

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

        BeginItemDrag(sender as DependencyObject, item);
    }

    private void BeginItemDrag(DependencyObject? source, ShelfItem item)
    {
        if (source is null)
        {
            return;
        }

        // The user is free to delete or move a file while it sits on the shelf.
        // Handing a dead path to another application produces a confusing error
        // in that application rather than in this one, so it is checked here.
        if (!item.StillExists())
        {
            _shelf.Remove(item);
            return;
        }

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { item.FullPath });

        // Some older targets, and most plain text fields, take the path as text.
        data.SetData(DataFormats.UnicodeText, item.FullPath);

        try
        {
            // Move is deliberately not offered. With a file drag the target
            // carries out the operation, and a target that chose Move would
            // delete the user's original file. The shelf holds a reference, not a
            // copy, so it has no business authorising that.
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Link);
        }
        catch (COMException)
        {
            // The shell refuses to start a drag while another one is already in
            // progress, which happens if the user is quick. There is nothing to
            // recover and nothing the user needs told.
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
