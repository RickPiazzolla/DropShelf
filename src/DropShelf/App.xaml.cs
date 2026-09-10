using System.Windows;

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
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Nothing owns the app's lifetime yet. Until the tray icon exists there is
        // no way for the user to quit, so exit immediately rather than leaving an
        // invisible process behind.
        Shutdown();
    }
}
