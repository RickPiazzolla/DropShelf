using System.Runtime.InteropServices;

namespace DropShelf.Interop;

/// <summary>
/// Answers whether a window is on the virtual desktop the user is looking at.
/// </summary>
/// <remarks>
/// Windows has no supported way to pin a window to every virtual desktop. The
/// interfaces that would allow it are undocumented and their identifiers change
/// between builds, so anything written against them stops working at the next
/// feature update.
/// <para>
/// DropShelf takes the supported route instead. It asks whether the shelf is on
/// the current desktop, and when it is not, the shelf window is thrown away and
/// rebuilt. A window created now is created here, which puts the shelf in front
/// of the user without guessing at private APIs. The items survive because they
/// live in the model rather than the window.
/// </para>
/// </remarks>
internal static class VirtualDesktop
{
    private static NativeMethods.IVirtualDesktopManager? _manager;
    private static bool _unavailable;

    /// <summary>
    /// True if the window is on the desktop currently being viewed, false if it
    /// is on another one, and null if the question cannot be answered.
    /// </summary>
    /// <remarks>
    /// The null case is not a failure to handle loudly. It happens on systems
    /// where the manager cannot be created, and the right response is simply to
    /// carry on as though the shelf were where it should be.
    /// </remarks>
    public static bool? IsWindowOnCurrentDesktop(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }

        var manager = GetManager();
        if (manager is null)
        {
            return null;
        }

        try
        {
            var hr = manager.IsWindowOnCurrentVirtualDesktop(window, out var onCurrent);
            return hr < 0 ? null : onCurrent;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static NativeMethods.IVirtualDesktopManager? GetManager()
    {
        if (_manager is not null || _unavailable)
        {
            return _manager;
        }

        try
        {
            var type = Type.GetTypeFromCLSID(NativeMethods.ClsidVirtualDesktopManager);
            if (type is not null && Activator.CreateInstance(type) is NativeMethods.IVirtualDesktopManager manager)
            {
                _manager = manager;
                return _manager;
            }
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException or TypeLoadException)
        {
            // Left deliberately quiet. Losing virtual desktop awareness degrades
            // one behaviour; it is not worth failing start-up over.
        }

        // Remembered, so a system without the manager does not pay for a failing
        // COM activation every single time the shelf is summoned.
        _unavailable = true;
        return null;
    }
}
