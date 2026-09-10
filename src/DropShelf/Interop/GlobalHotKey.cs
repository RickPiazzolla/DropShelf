using System.Windows.Input;

namespace DropShelf.Interop;

/// <summary>
/// A system wide keyboard shortcut, active whatever application has focus.
/// </summary>
/// <remarks>
/// Registration can fail, and that is not an error worth stopping start-up over.
/// Windows gives a combination to whoever asks for it first, so if another
/// application already owns it there is nothing to be done except carry on
/// without it. <see cref="IsRegistered"/> reports which happened.
/// </remarks>
internal sealed class GlobalHotKey : IDisposable
{
    // Any small number will do. It only has to be unique among the hot keys this
    // one window registers.
    private const int HotKeyId = 0xD509;

    private readonly MessageWindow _window;
    private bool _disposed;

    public event EventHandler? Pressed;

    public GlobalHotKey(MessageWindow window, NativeMethods.HotKeyModifiers modifiers, Key key)
    {
        _window = window;

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        IsRegistered = NativeMethods.RegisterHotKey(
            _window.Handle,
            HotKeyId,
            modifiers | NativeMethods.HotKeyModifiers.NoRepeat,
            virtualKey);

        if (IsRegistered)
        {
            _window.MessageReceived += OnMessage;
        }
    }

    /// <summary>
    /// False when another application already owns the combination.
    /// </summary>
    public bool IsRegistered { get; }

    private void OnMessage(object? sender, MessageEventArgs e)
    {
        if (e.Message != NativeMethods.WM_HOTKEY || e.WParam.ToInt32() != HotKeyId)
        {
            return;
        }

        e.Handled = true;
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!IsRegistered)
        {
            return;
        }

        _window.MessageReceived -= OnMessage;

        // Registrations are owned by the window, so they would go away with it
        // anyway. Releasing explicitly means the combination is free again the
        // moment the user quits rather than whenever the handle is torn down.
        NativeMethods.UnregisterHotKey(_window.Handle, HotKeyId);
    }
}
