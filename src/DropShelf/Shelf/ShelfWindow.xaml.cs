using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using DropShelf.Model;

namespace DropShelf.Shelf;

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
    private readonly Model.Shelf _shelf;
    private bool _allowClose;

    public ShelfWindow(Model.Shelf shelf)
    {
        _shelf = shelf;

        InitializeComponent();

        DataContext = _shelf;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        MoveToDefaultPosition();
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

    private void OnCardDragOver(object sender, DragEventArgs e)
    {
        // Copy rather than Move. Move would tell the source application that
        // DropShelf has taken ownership, and Explorer acts on that by deleting
        // the original once the drop completes. The shelf only records a path.
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        // Without this the parent chain gets a say and can override the effect,
        // which shows the user the wrong cursor.
        e.Handled = true;
    }

    private void OnCardDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _shelf.AddPaths(paths);
        }

        e.Handled = true;
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
        if (sender is FrameworkElement { DataContext: ShelfItem item })
        {
            _pressOrigin = e.GetPosition(null);
            _pressedItem = item;
        }
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
