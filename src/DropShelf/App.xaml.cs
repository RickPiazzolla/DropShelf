using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DropShelf.DropHandling;
using DropShelf.Interop;
using DropShelf.Model;
using DropShelf.Settings;
using DropShelf.Tray;
using DropShelf.Ui;

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
    private Shelf? _shelf;
    private ThumbnailLoader? _thumbnails;
    private MessageWindow? _messageWindow;
    private GlobalHotKey? _hotKey;
    private StagingArea? _staging;
    private DropReader? _dropReader;
    private CatcherWindow? _catcher;
    private EdgeDragWatcher? _dragWatcher;
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The default, OnLastWindowClose, would end the process the moment the
        // shelf is dismissed. The tray icon owns the lifetime instead.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Constructed on the UI thread on purpose. It captures this dispatcher so
        // that finished thumbnails come back on the thread allowed to touch them.
        _thumbnails = new ThumbnailLoader();

        _settings = AppSettings.Load();

        // The registry is the truth about whether Windows will actually start the
        // app, not the settings file. They can disagree if the user removed the
        // entry through Task Manager, and the registry wins.
        _settings.StartWithWindows = StartupRegistration.IsEnabled();

        _staging = new StagingArea();
        _dropReader = new DropReader(_staging);

        _shelf = new Shelf(_thumbnails);

        if (_settings.RememberShelf)
        {
            // Paths that no longer resolve are dropped silently by AddPaths, so a
            // file deleted since last time simply does not come back.
            _shelf.AddPaths(ShelfStore.Load());
        }

        _shelfWindow = CreateShelfWindow();

        _catcher = new CatcherWindow(_shelf, _dropReader);
        _catcher.Caught += OnCaught;

        // Watches raw mouse input for a drag pushed against the right edge of the
        // screen, which is the only signal Windows offers that a drag is underway.
        _dragWatcher = new EdgeDragWatcher();
        _dragWatcher.EdgeReached += OnEdgeReached;
        _dragWatcher.DragEnded += OnDragEnded;

        _messageWindow = new MessageWindow("DropShelf.Messages");

        _hotKey = new GlobalHotKey(
            _messageWindow,
            NativeMethods.HotKeyModifiers.Control | NativeMethods.HotKeyModifiers.Shift,
            Key.D);
        _hotKey.Pressed += OnHotKeyPressed;

        _trayIcon = new TrayIcon();
        _trayIcon.ToggleShelfRequested += OnToggleShelfRequested;
        _trayIcon.OpenStagingRequested += OnOpenStagingRequested;
        _trayIcon.StartWithWindowsToggled += OnStartWithWindowsToggled;
        _trayIcon.RememberShelfToggled += OnRememberShelfToggled;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.ShowSettings(_settings.StartWithWindows, _settings.RememberShelf);
    }

    private void OnStartWithWindowsToggled(object? sender, bool enabled)
    {
        if (!StartupRegistration.Set(enabled))
        {
            // The registry write failed, so the tick would be a lie. Put it back.
            _trayIcon?.ShowSettings(StartupRegistration.IsEnabled(), _settings.RememberShelf);
            return;
        }

        _settings.StartWithWindows = enabled;
        _settings.Save();
    }

    private void OnRememberShelfToggled(object? sender, bool enabled)
    {
        _settings.RememberShelf = enabled;
        _settings.Save();

        if (!enabled)
        {
            // Turning it off should take effect now rather than at the next exit,
            // otherwise a file the user wanted forgotten sits on disk until then.
            ShelfStore.Clear();
        }
    }

    private ShelfWindow CreateShelfWindow()
    {
        var window = new ShelfWindow(_shelf!, _dropReader!);

        if (_settings.ShelfLeft is { } left && _settings.ShelfTop is { } top && IsOnAScreen(left, top))
        {
            window.RestorePosition(left, top);
        }

        // Forces the underlying window to exist without showing it, so the
        // virtual desktop check has a handle to ask about.
        new WindowInteropHelper(window).EnsureHandle();

        return window;
    }

    /// <summary>
    /// Guards against restoring the shelf onto a monitor that is no longer there.
    /// </summary>
    /// <remarks>
    /// Someone who undocks a laptop would otherwise find the shelf reappearing on
    /// a screen that no longer exists, with no way to get it back.
    /// </remarks>
    private static bool IsOnAScreen(double left, double top)
    {
        const double MinimumVisible = 60;

        var bounds = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        return bounds.Contains(new Point(left + MinimumVisible, top + MinimumVisible));
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

    private void OnEdgeReached(object? sender, EventArgs e)
    {
        // The hook callback runs on whichever thread saw the mouse event, which
        // is not necessarily this one, and windows can only be touched here.
        Dispatcher.BeginInvoke(() => _catcher?.ShowCatcher());
    }

    private void OnDragEnded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // A drop that landed on the catcher has already hidden it. This
            // covers the far more common case of the gesture being a false alarm,
            // such as selecting text out to the edge of the screen.
            _catcher?.HideCatcher();
        });
    }

    private void OnCaught(object? sender, EventArgs e) => ShowShelfHere();

    private void OnOpenStagingRequested(object? sender, EventArgs e)
    {
        if (_staging is null)
        {
            return;
        }

        try
        {
            // Created on demand. Nothing may ever have been staged, and an
            // Explorer window reporting a missing folder is a poor answer to
            // "show me the folder".
            Directory.CreateDirectory(_staging.Root);
            Process.Start(new ProcessStartInfo(_staging.Root) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            // Nothing useful to say, and no reason to interrupt.
        }
    }

    private void OnExitRequested(object? sender, EventArgs e) => Shutdown();

    /// <summary>
    /// Records the shelf contents and window position on the way out.
    /// </summary>
    /// <remarks>
    /// Saved once at exit rather than on every change. The shelf is small, exit is
    /// the only moment the state is final, and writing a file on each drop would
    /// mean touching the disk during a drag.
    /// </remarks>
    private void SaveState()
    {
        if (_shelfWindow is not null)
        {
            _settings.ShelfLeft = _shelfWindow.Left;
            _settings.ShelfTop = _shelfWindow.Top;
        }

        _settings.Save();

        if (_settings.RememberShelf && _shelf is not null)
        {
            ShelfStore.Save(_shelf.Items.Select(item => item.FullPath));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveState();

        _trayIcon?.Dispose();
        _trayIcon = null;

        _hotKey?.Dispose();
        _hotKey = null;

        _messageWindow?.Dispose();
        _messageWindow = null;

        // Before the windows, because the hook fires into them.
        _dragWatcher?.Dispose();
        _dragWatcher = null;

        _catcher?.CloseForShutdown();
        _catcher = null;

        _shelfWindow?.CloseForShutdown();
        _shelfWindow = null;

        _thumbnails?.Dispose();
        _thumbnails = null;

        base.OnExit(e);
    }
}
