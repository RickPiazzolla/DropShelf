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
