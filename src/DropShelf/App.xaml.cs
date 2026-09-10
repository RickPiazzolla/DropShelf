using System.Windows;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The default, OnLastWindowClose, would end the process the moment the
        // shelf is dismissed. The tray icon owns the lifetime instead.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _trayIcon = new TrayIcon();
        _trayIcon.ExitRequested += OnExitRequested;
    }

    private void OnExitRequested(object? sender, EventArgs e) => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        base.OnExit(e);
    }
}
