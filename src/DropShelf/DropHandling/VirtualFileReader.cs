using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DropShelf.Interop;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DropShelf.DropHandling;

/// <summary>
/// Pulls files out of a drop that only promised them.
/// </summary>
/// <remarks>
/// An email client dragging an attachment does not put a file on disk first. It
/// offers a list of names in a format called FileGroupDescriptorW, and undertakes
/// to produce the bytes for any of them on request through FileContents. Outlook,
/// Gmail in a browser, and most archive tools all work this way.
/// <para>
/// WPF's own data object cannot ask for these. Its GetData has no way to say which
/// file in the group is wanted, and the request has to name an index. Getting at
/// that means dropping down to the underlying COM interface.
/// </para>
/// </remarks>
internal static class VirtualFileReader
{
    private const string DescriptorFormat = "FileGroupDescriptorW";
    private const string ContentsFormat = "FileContents";

    public static bool IsPresent(System.Windows.IDataObject data)
        => data.GetDataPresent(DescriptorFormat) && data.GetDataPresent(ContentsFormat);

    /// <summary>
    /// Writes every promised file into <paramref name="folder"/> and returns the
    /// paths of those that arrived.
    /// </summary>
    /// <remarks>
    /// Files that fail individually are skipped rather than abandoning the whole
    /// drop. A message with five attachments where one cannot be produced should
    /// still put the other four on the shelf.
    /// </remarks>
    public static List<string> Extract(System.Windows.IDataObject data, string folder)
    {
        var written = new List<string>();

        var names = ReadFileNames(data);
        if (names.Count == 0 || data is not ComIDataObject comData)
        {
            return written;
        }

        for (var index = 0; index < names.Count; index++)
        {
            try
            {
                var destination = StagingArea.SafePathFor(folder, names[index]);
                if (TryWriteContents(comData, index, destination))
                {
                    written.Add(destination);
                }
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ExternalException)
            {
                // One attachment failing is not a reason to lose the others.
            }
        }

        return written;
    }

    private static List<string> ReadFileNames(System.Windows.IDataObject data)
    {
        var names = new List<string>();

        if (data.GetData(DescriptorFormat) is not MemoryStream stream)
        {
            return names;
        }

        using (stream)
        {
            var raw = stream.ToArray();
            if (raw.Length < sizeof(uint))
            {
                return names;
            }

            // The block starts with a count, then that many fixed size
            // descriptors packed one after another.
            var count = BitConverter.ToUInt32(raw, 0);
            var descriptorSize = Marshal.SizeOf<NativeMethods.FILEDESCRIPTORW>();

            var handle = GCHandle.Alloc(raw, GCHandleType.Pinned);
            try
            {
                var basePointer = handle.AddrOfPinnedObject();

                for (var i = 0; i < count; i++)
                {
                    var offset = sizeof(uint) + (i * descriptorSize);
                    if (offset + descriptorSize > raw.Length)
                    {
                        // The block is shorter than its own count claims. Take
                        // what is actually there rather than reading past the end.
                        break;
                    }

                    var descriptor = Marshal.PtrToStructure<NativeMethods.FILEDESCRIPTORW>(basePointer + offset);
                    names.Add(descriptor.cFileName);
                }
            }
            finally
            {
                handle.Free();
            }
        }

        return names;
    }

    private static bool TryWriteContents(ComIDataObject comData, int index, string destination)
    {
        var format = new FORMATETC
        {
            cfFormat = (short)System.Windows.DataFormats.GetDataFormat(ContentsFormat).Id,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            ptd = IntPtr.Zero,

            // Sources differ on how they hand the bytes over. Asking for both
            // means the source picks whichever it already has.
            tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL,
        };

        comData.GetData(ref format, out var medium);

        try
        {
            switch (medium.tymed)
            {
                case TYMED.TYMED_ISTREAM:
                    if (medium.unionmember == IntPtr.Zero)
                    {
                        return false;
                    }

                    var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                    try
                    {
                        WriteStream(stream, destination);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(stream);
                    }

                    return true;

                case TYMED.TYMED_HGLOBAL:
                    return WriteGlobalMemory(medium.unionmember, destination);

                default:
                    return false;
            }
        }
        finally
        {
            // The medium is owned by this side once GetData returns. Not releasing
            // it leaks whatever the source allocated, which for a large attachment
            // is the whole file.
            NativeMethods.ReleaseStgMedium(ref medium);
        }
    }

    private static void WriteStream(IStream stream, string destination)
    {
        const int BufferSize = 81920;

        var buffer = new byte[BufferSize];

        // IStream reports how much it read through a pointer rather than a return
        // value, so a small piece of unmanaged memory has to be provided for it.
        var readCount = Marshal.AllocCoTaskMem(sizeof(int));

        try
        {
            using var output = File.Create(destination);

            while (true)
            {
                stream.Read(buffer, BufferSize, readCount);
                var read = Marshal.ReadInt32(readCount);

                if (read <= 0)
                {
                    break;
                }

                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(readCount);
        }
    }

    private static bool WriteGlobalMemory(IntPtr handle, string destination)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var size = (long)NativeMethods.GlobalSize(handle);
            if (size <= 0)
            {
                return false;
            }

            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, (int)size);
            File.WriteAllBytes(destination, bytes);
            return true;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }
}
