using System.Windows;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The default, OnLastWindowClose, would end the process the moment the
        // shelf is dismissed. The tray icon owns the lifetime instead.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Constructed but not shown. Building it up front means the first summon
        // is instant, and it gives the shelf somewhere to hold items before the
        // user has ever looked at it.
        _shelfWindow = new ShelfWindow();

        _trayIcon = new TrayIcon();
        _trayIcon.ToggleShelfRequested += OnToggleShelfRequested;
        _trayIcon.ExitRequested += OnExitRequested;
    }

    private void OnToggleShelfRequested(object? sender, EventArgs e) => _shelfWindow?.ToggleShelf();

    private void OnExitRequested(object? sender, EventArgs e) => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        _shelfWindow?.CloseForShutdown();
        _shelfWindow = null;

        base.OnExit(e);
    }
}
