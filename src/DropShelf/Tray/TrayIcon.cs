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
    private readonly ToolStripMenuItem _startWithWindowsItem;
    private readonly ToolStripMenuItem _rememberShelfItem;
    private readonly Icon _icon;
    private bool _disposed;

    /// <summary>
    /// Raised when the user asks to see or dismiss the shelf.
    /// </summary>
    public event EventHandler? ToggleShelfRequested;

    /// <summary>
    /// Raised when the user asks to see where staged files are kept.
    /// </summary>
    public event EventHandler? OpenStagingRequested;

    /// <summary>
    /// Raised with the new value when a settings toggle is switched.
    /// </summary>
    public event EventHandler<bool>? StartWithWindowsToggled;

    public event EventHandler<bool>? RememberShelfToggled;

    /// <summary>
    /// Raised when the user chooses to quit.
    /// </summary>
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _icon = LoadTrayIcon();

        _menu = new ContextMenuStrip();

        var toggleItem = new ToolStripMenuItem("Show or hide shelf");
        toggleItem.Click += (_, _) => ToggleShelfRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(toggleItem);

        _menu.Items.Add(new ToolStripSeparator());

        // Checkable menu items rather than a settings window. There are two
        // choices to make, and a whole dialog to hold two checkboxes would be more
        // ceremony than the decisions deserve.
        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startWithWindowsItem.CheckedChanged += OnStartWithWindowsChanged;
        _menu.Items.Add(_startWithWindowsItem);

        _rememberShelfItem = new ToolStripMenuItem("Remember shelf between sessions") { CheckOnClick = true };
        _rememberShelfItem.CheckedChanged += OnRememberShelfChanged;
        _menu.Items.Add(_rememberShelfItem);

        _menu.Items.Add(new ToolStripSeparator());

        // Content dropped from a browser or an email client has to be written to
        // disk before the shelf can hold it, and none of it is ever deleted
        // automatically. This is how the user finds it to clear it out.
        var stagingItem = new ToolStripMenuItem("Open saved drops folder");
        stagingItem.Click += (_, _) => OpenStagingRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(stagingItem);

        _menu.Items.Add(new ToolStripSeparator());

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

        _notifyIcon.MouseClick += OnMouseClick;
    }

    /// <summary>
    /// Sets the tick marks to match stored settings without raising the toggle
    /// events, which would otherwise write the value straight back on start-up.
    /// </summary>
    public void ShowSettings(bool startWithWindows, bool rememberShelf)
    {
        _startWithWindowsItem.CheckedChanged -= OnStartWithWindowsChanged;
        _rememberShelfItem.CheckedChanged -= OnRememberShelfChanged;

        _startWithWindowsItem.Checked = startWithWindows;
        _rememberShelfItem.Checked = rememberShelf;

        _startWithWindowsItem.CheckedChanged += OnStartWithWindowsChanged;
        _rememberShelfItem.CheckedChanged += OnRememberShelfChanged;
    }

    private void OnStartWithWindowsChanged(object? sender, EventArgs e)
        => StartWithWindowsToggled?.Invoke(this, _startWithWindowsItem.Checked);

    private void OnRememberShelfChanged(object? sender, EventArgs e)
        => RememberShelfToggled?.Invoke(this, _rememberShelfItem.Checked);

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        // Right click is left alone so that Windows can open the context menu
        // itself, which it does with the correct placement and dismissal
        // behaviour for the notification area.
        if (e.Button == MouseButtons.Left)
        {
            ToggleShelfRequested?.Invoke(this, EventArgs.Empty);
        }
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
