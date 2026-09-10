namespace DropShelf.Interop;

/// <summary>
/// Notices when the user drags something against the right hand edge of the
/// screen.
/// </summary>
/// <remarks>
/// Windows does not tell other applications that a drag has started. The only way
/// to know is to watch raw mouse input through a low level hook, which sees every
/// event on the system before any application does.
/// <para>
/// The gesture is deliberately narrow. Holding the button while moving describes
/// selecting text as much as it describes dragging a file, so triggering on that
/// alone would make the catcher appear constantly. Requiring the pointer to reach
/// the screen edge makes it something the user does on purpose.
/// </para>
/// </remarks>
internal sealed class EdgeDragWatcher : IDisposable
{
    /// <summary>How close to the edge counts, in physical pixels.</summary>
    private const int EdgeThickness = 3;

    /// <summary>
    /// How far the pointer must travel after the press before the gesture counts,
    /// so that a plain click near the edge does nothing.
    /// </summary>
    private const int MinimumTravel = 40;

    // Held in a field for the lifetime of the hook. Passing a delegate straight
    // into SetWindowsHookEx lets the garbage collector reclaim it while Windows
    // still holds the function pointer, which crashes the process at some
    // unrelated later moment.
    private readonly NativeMethods.LowLevelMouseProc _callback;

    private IntPtr _hook;
    private bool _buttonDown;
    private NativeMethods.POINT _pressedAt;
    private bool _triggered;
    private bool _disposed;

    /// <summary>Raised when the pointer reaches the edge with the button held.</summary>
    public event EventHandler? EdgeReached;

    /// <summary>Raised when the button is released, whatever happened before it.</summary>
    public event EventHandler? DragEnded;

    public EdgeDragWatcher()
    {
        _callback = OnMouseEvent;

        // A module handle of zero is correct for a managed hook in the process
        // that installs it, and a thread id of zero makes it system wide.
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _callback, IntPtr.Zero, 0);
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    private IntPtr OnMouseEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        // Everything in here runs inside every mouse event on the machine. It has
        // to stay cheap: no allocation, no blocking, and no work that could wait.
        if (code < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        switch (wParam.ToInt32())
        {
            case NativeMethods.WM_LBUTTONDOWN:
                _buttonDown = true;
                _triggered = false;
                _pressedAt = ReadPoint(lParam);
                break;

            case NativeMethods.WM_MOUSEMOVE:
                if (_buttonDown && !_triggered && IsGesture(ReadPoint(lParam)))
                {
                    _triggered = true;
                    EdgeReached?.Invoke(this, EventArgs.Empty);
                }

                break;

            case NativeMethods.WM_LBUTTONUP:
                if (_buttonDown)
                {
                    _buttonDown = false;
                    DragEnded?.Invoke(this, EventArgs.Empty);
                }

                break;
        }

        // Always pass the event on. Swallowing mouse input from a global hook
        // would break every other application on the system.
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static NativeMethods.POINT ReadPoint(IntPtr lParam)
    {
        // Read as a raw struct rather than marshalled, because this runs on every
        // mouse move and marshalling would allocate each time.
        return System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam).pt;
    }

    private bool IsGesture(NativeMethods.POINT current)
    {
        var travelledX = current.x - _pressedAt.x;
        var travelledY = current.y - _pressedAt.y;

        if ((travelledX * travelledX) + (travelledY * travelledY) < MinimumTravel * MinimumTravel)
        {
            return false;
        }

        // Physical pixels, which is what the hook reports. The app is per monitor
        // DPI aware, so these system metrics are true pixels rather than a scaled
        // approximation.
        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var rightEdge = virtualLeft + virtualWidth - 1;

        return current.x >= rightEdge - EdgeThickness;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
