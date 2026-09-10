using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DropShelf.Interop;
using DropShelf.Shelf;
using DropShelf.Tray;

namespace DropShelf;

/// <summary>
/// Application entry point.
/// </summary>
/// <remarks>
/// DropShelf has no start-up window on purpose. It is a background utility: it
/// lives in the notification area and only shows a window when the user asks for
/// one, or when a drag needs somewhere to land. Because of that there is no
/// <c>StartupUri</c> here, and the shutdown mode cannot be left at its default of
/// closing with the last window.
/// </remarks>
public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private ShelfWindow? _shelfWindow;
    private Model.Shelf? _shelf;
    private ThumbnailLoader? _thumbnails;
    private MessageWindow? _messageWindow;
    private GlobalHotKey? _hotKey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The default, OnLastWindowClose, would end the process the moment the
        // shelf is dismissed. The tray icon owns the lifetime instead.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Constructed on the UI thread on purpose. It captures this dispatcher so
        // that finished thumbnails come back on the thread allowed to touch them.
        _thumbnails = new ThumbnailLoader();

        _shelf = new Model.Shelf(_thumbnails);
        _shelfWindow = CreateShelfWindow();

        _messageWindow = new MessageWindow("DropShelf.Messages");

        _hotKey = new GlobalHotKey(
            _messageWindow,
            NativeMethods.HotKeyModifiers.Control | NativeMethods.HotKeyModifiers.Shift,
            Key.D);
        _hotKey.Pressed += OnHotKeyPressed;

        _trayIcon = new TrayIcon();
        _trayIcon.ToggleShelfRequested += OnToggleShelfRequested;
        _trayIcon.ExitRequested += OnExitRequested;
    }

    private ShelfWindow CreateShelfWindow()
    {
        var window = new ShelfWindow(_shelf!);

        // Forces the underlying window to exist without showing it, so the
        // virtual desktop check has a handle to ask about.
        new WindowInteropHelper(window).EnsureHandle();

        return window;
    }

    private void OnHotKeyPressed(object? sender, EventArgs e) => ToggleShelf();

    private void OnToggleShelfRequested(object? sender, EventArgs e) => ToggleShelf();

    private void ToggleShelf()
    {
        if (_shelfWindow is null)
        {
            return;
        }

        if (_shelfWindow.IsVisible && IsShelfOnCurrentDesktop())
        {
            _shelfWindow.HideShelf();
            return;
        }

        ShowShelfHere();
    }

    /// <summary>
    /// Shows the shelf on the desktop the user is currently looking at.
    /// </summary>
    private void ShowShelfHere()
    {
        if (_shelfWindow is null)
        {
            return;
        }

        if (!IsShelfOnCurrentDesktop())
        {
            _shelfWindow = ReplaceShelfWindow(_shelfWindow);
        }

        _shelfWindow.ShowShelf();
    }

    private bool IsShelfOnCurrentDesktop()
    {
        if (_shelfWindow is null)
        {
            return false;
        }

        var handle = new WindowInteropHelper(_shelfWindow).Handle;

        // Null means the question could not be answered, which is treated as yes.
        // Rebuilding the window on every summon because virtual desktop support
        // is missing would be far worse than the problem it solves.
        return VirtualDesktop.IsWindowOnCurrentDesktop(handle) ?? true;
    }

    /// <summary>
    /// Destroys the shelf window and builds a fresh one, which lands on whichever
    /// virtual desktop is in front of the user right now.
    /// </summary>
    /// <remarks>
    /// The items are untouched. They belong to the shelf model, which outlives any
    /// particular window.
    /// </remarks>
    private ShelfWindow ReplaceShelfWindow(ShelfWindow existing)
    {
        var left = existing.Left;
        var top = existing.Top;
        var wasVisible = existing.IsVisible;

        existing.CloseForShutdown();

        var replacement = CreateShelfWindow();
        replacement.RestorePosition(left, top);

        if (wasVisible)
        {
            replacement.ShowShelf();
        }

        return replacement;
    }

    private void OnExitRequested(object? sender, EventArgs e) => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        _hotKey?.Dispose();
        _hotKey = null;

        _messageWindow?.Dispose();
        _messageWindow = null;

        _shelfWindow?.CloseForShutdown();
        _shelfWindow = null;

        _thumbnails?.Dispose();
        _thumbnails = null;

        base.OnExit(e);
    }
}
