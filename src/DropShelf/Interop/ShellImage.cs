using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DropShelf.Interop;

/// <summary>
/// Asks the Windows shell for the picture it would show for a file.
/// </summary>
/// <remarks>
/// Must be called from a thread in a single threaded apartment. Shell COM objects
/// are apartment threaded and will not marshal onto an arbitrary pool thread.
/// </remarks>
internal static class ShellImage
{
    /// <summary>
    /// Returns a frozen image for the given path, or null if the shell has
    /// nothing to offer.
    /// </summary>
    /// <remarks>
    /// A real preview is tried first and the file type icon is the fallback.
    /// Asking for the two separately rather than letting the shell choose means a
    /// photo gets its own contents rather than a generic picture icon, while a
    /// text file, which has no preview, still gets something to show.
    /// </remarks>
    public static BitmapSource? TryLoad(string path, int pixelSize)
    {
        object item;
        var iid = NativeMethods.IidShellItemImageFactory;

        try
        {
            NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, in iid, out item);
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        {
            // The file went away between being dropped and being drawn.
            return null;
        }

        if (item is not NativeMethods.IShellItemImageFactory factory)
        {
            return null;
        }

        try
        {
            return Extract(factory, pixelSize, NativeMethods.SIIGBF.ResizeToFit | NativeMethods.SIIGBF.ThumbnailOnly)
                ?? Extract(factory, pixelSize, NativeMethods.SIIGBF.ResizeToFit | NativeMethods.SIIGBF.IconOnly);
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    private static BitmapSource? Extract(NativeMethods.IShellItemImageFactory factory, int pixelSize, NativeMethods.SIIGBF flags)
    {
        var hr = factory.GetImage(new NativeMethods.SIZE(pixelSize, pixelSize), flags, out var handle);
        if (hr < 0 || handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Convert(handle);
        }
        finally
        {
            // The caller owns the bitmap the shell just created. Leaking these
            // would exhaust the GDI object quota, which has a per process limit
            // of ten thousand and produces baffling failures once reached.
            NativeMethods.DeleteObject(handle);
        }
    }

    private static BitmapSource? Convert(IntPtr handle)
    {
        if (NativeMethods.GetObject(handle, Marshal.SizeOf<NativeMethods.BITMAP>(), out var info) == 0)
        {
            return null;
        }

        // The framework has its own converter, but it treats every bitmap as
        // opaque and throws the alpha channel away, which leaves icons with black
        // corners where they should be transparent. Copying the bits by hand is
        // the only way to keep them.
        if (info.bmBits == IntPtr.Zero || info.bmBitsPixel != 32 || info.bmWidth <= 0 || info.bmHeight <= 0)
        {
            // Fully qualified. A plain "using System.Windows.Interop" would be
            // shadowed by this file's own DropShelf.Interop namespace.
            var opaque = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                handle, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            opaque.Freeze();
            return opaque;
        }

        var stride = info.bmWidthBytes;
        var length = stride * info.bmHeight;
        var pixels = new byte[length];
        Marshal.Copy(info.bmBits, pixels, 0, length);

        // Some sources hand back 32 bits per pixel with the alpha byte left at
        // zero throughout, meaning "no alpha information" rather than "fully
        // transparent". Taking that at face value renders the whole tile blank.
        var hasAlpha = false;
        for (var i = 3; i < length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
        var source = BitmapSource.Create(info.bmWidth, info.bmHeight, 96, 96, format, null, pixels, stride);

        // Frozen so it can be handed to the UI thread. An unfrozen BitmapSource
        // belongs to the thread that made it and cannot cross.
        source.Freeze();
        return source;
    }
}
