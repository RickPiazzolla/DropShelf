using System.Runtime.InteropServices;

namespace DropShelf.Interop;

/// <summary>
/// Declarations for the Windows APIs DropShelf calls directly.
/// </summary>
/// <remarks>
/// Everything here is internal. These are raw platform calls with no argument
/// checking, and nothing outside this assembly has any business reaching them.
/// </remarks>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE(int width, int height)
    {
        public int cx = width;
        public int cy = height;
    }

    /// <summary>
    /// Flags controlling how the shell produces an image for an item.
    /// </summary>
    [Flags]
    internal enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,

        /// <summary>Never produce a thumbnail, only the file type icon.</summary>
        IconOnly = 0x04,

        /// <summary>Fail rather than falling back to an icon.</summary>
        ThumbnailOnly = 0x08,

        InCacheOnly = 0x10,
        ScaleUp = 0x100,
    }

    /// <summary>
    /// Produces a thumbnail or icon for a shell item.
    /// </summary>
    /// <remarks>
    /// This is what File Explorer itself uses, which is why the results match what
    /// the user already sees, including previews supplied by third party handlers
    /// such as PDF readers and photo tools.
    /// </remarks>
    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        // PreserveSig, because a missing thumbnail is an expected outcome rather
        // than an error. Letting the runtime turn it into an exception would mean
        // using exceptions for ordinary control flow on every single icon.
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    internal static readonly Guid IidShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object item);

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    internal static extern int GetObject(IntPtr handle, int bufferSize, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr handle);

    internal const int WM_HOTKEY = 0x0312;

    [Flags]
    internal enum HotKeyModifiers : uint
    {
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Windows = 0x0008,

        /// <summary>
        /// Suppresses the repeat messages Windows would otherwise send while the
        /// combination is held down.
        /// </summary>
        NoRepeat = 0x4000,
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, HotKeyModifiers modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);

    /// <summary>
    /// Reports and changes which virtual desktop a window belongs to.
    /// </summary>
    /// <remarks>
    /// This is the only supported virtual desktop API. The richer interfaces that
    /// would allow pinning a window to every desktop are undocumented and their
    /// interface identifiers change between Windows builds, so an app built
    /// against them breaks on the next feature update.
    /// </remarks>
    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, in Guid desktopId);
    }

    internal static readonly Guid ClsidVirtualDesktopManager = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    /// <summary>
    /// One entry in a FILEGROUPDESCRIPTORW, describing a file that exists only
    /// inside the application being dragged from.
    /// </summary>
    /// <remarks>
    /// The layout has to match the C declaration byte for byte, because the source
    /// application writes it into shared memory and this is the only agreement
    /// about what those bytes mean. It comes to 592 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FILEDESCRIPTORW
    {
        public uint dwFlags;
        public Guid clsid;
        public int sizelCx;
        public int sizelCy;
        public int pointlX;
        public int pointlY;
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
    }

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern UIntPtr GlobalSize(IntPtr handle);

    [DllImport("ole32.dll")]
    internal static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);

    internal const int WH_MOUSE_LL = 14;
    internal const int WM_MOUSEMOVE = 0x0200;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    internal delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_CXVIRTUALSCREEN = 78;

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);
}
