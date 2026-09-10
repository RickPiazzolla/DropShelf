using System.Windows.Interop;

namespace DropShelf.Interop;

/// <summary>
/// An invisible window that exists purely to receive Windows messages.
/// </summary>
/// <remarks>
/// Some Windows features are addressed to a window handle rather than to a
/// process, and a global hot key is one of them. The obvious handle to use would
/// be the shelf's, but the shelf gets destroyed and rebuilt when it has to move
/// between virtual desktops, and a hot key registered against a destroyed handle
/// silently stops working.
/// <para>
/// This window is created once at start-up and lives until the app exits, which
/// gives those registrations somewhere stable to point.
/// </para>
/// </remarks>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private bool _disposed;

    /// <summary>
    /// Raised for every message. Set <c>Handled</c> to stop further processing.
    /// </summary>
    public event EventHandler<MessageEventArgs>? MessageReceived;

    public MessageWindow(string name)
    {
        var parameters = new HwndSourceParameters(name)
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(OnMessage);
    }

    public IntPtr Handle => _source.Handle;

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var args = new MessageEventArgs(message, wParam, lParam);
        MessageReceived?.Invoke(this, args);

        handled = args.Handled;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(OnMessage);
        _source.Dispose();
    }
}

internal sealed class MessageEventArgs(int message, IntPtr wParam, IntPtr lParam) : EventArgs
{
    public int Message { get; } = message;

    public IntPtr WParam { get; } = wParam;

    public IntPtr LParam { get; } = lParam;

    public bool Handled { get; set; }
}
