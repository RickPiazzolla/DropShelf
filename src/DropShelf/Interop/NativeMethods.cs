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
}
