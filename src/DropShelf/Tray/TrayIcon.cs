using System.Drawing;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace DropShelf.Tray;

/// <summary>
/// Owns the notification area icon and the menu behind it.
/// </summary>
/// <remarks>
/// This is the only part of DropShelf the user can always reach. The shelf itself
/// can be hidden, dragged off screen, or sitting on another virtual desktop, so
/// the tray icon is the way back. It is created first at start-up and disposed
/// last on the way out.
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _icon;
    private bool _disposed;

    /// <summary>
    /// Raised when the user chooses to quit.
    /// </summary>
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _icon = LoadTrayIcon();

        _menu = new ContextMenuStrip();
        var exitItem = new ToolStripMenuItem("Exit DropShelf");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            // Windows truncates this at 63 characters.
            Text = "DropShelf",
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    private static Icon LoadTrayIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/DropShelf.ico", UriKind.Absolute);
        var resource = WpfApplication.GetResourceStream(uri)
            ?? throw new InvalidOperationException("The tray icon is missing from the assembly resources.");

        using var stream = resource.Stream;

        // Ask for the exact size the notification area is using. Left to itself,
        // Icon picks the largest entry in the file and Windows scales the 256px
        // artwork down to 16px, which looks noticeably softer than the 16px entry
        // that was drawn for the job.
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Hiding before disposing matters. An undisposed icon stays in the tray
        // as a dead entry until the user happens to move the mouse over it, which
        // is how a process that has already exited leaves a ghost behind.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
