using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DropShelf.DropHandling;

/// <summary>
/// Turns whatever was dropped into a list of file paths.
/// </summary>
/// <remarks>
/// A single drop usually carries the same content several times over in different
/// shapes. Dragging an image out of Chrome offers the picture itself, the address
/// it came from, the surrounding HTML, and the alt text, all at once. The source
/// lists them roughly best first, but not reliably, so the order below is chosen
/// here rather than taken on trust.
/// <para>
/// The rule is to prefer whatever loses the least. A real file on disk beats a
/// promise of one, a promise beats raw pixels, pixels beat the address they came
/// from, and the address beats a plain text description of it.
/// </para>
/// </remarks>
public sealed class DropReader(StagingArea staging)
{
    private const string UrlFormat = "UniformResourceLocatorW";
    private const string PngFormat = "PNG";

    private readonly StagingArea _staging = staging;

    /// <summary>
    /// True if there is anything here worth putting on a shelf.
    /// </summary>
    public static bool CanRead(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop)
            || VirtualFileReader.IsPresent(data)
            || data.GetDataPresent(PngFormat)
            || data.GetDataPresent(DataFormats.Bitmap)
            || data.GetDataPresent(UrlFormat)
            || data.GetDataPresent(DataFormats.UnicodeText);

    public IReadOnlyList<string> Read(IDataObject data)
    {
        // Already files. Nothing to write, and nothing to lose.
        if (data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            return paths;
        }

        if (VirtualFileReader.IsPresent(data))
        {
            var extracted = VirtualFileReader.Extract(data, _staging.CreateBatchFolder());
            if (extracted.Count > 0)
            {
                return extracted;
            }
        }

        var image = TryReadImage(data);
        if (image is not null)
        {
            return SaveImage(image);
        }

        var url = TryReadUrl(data);
        if (url is not null)
        {
            return SaveUrl(url);
        }

        if (data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
        {
            return SaveText(text);
        }

        return [];
    }

    private static BitmapSource? TryReadImage(IDataObject data)
    {
        // PNG first. It is what browsers offer for a picture and it keeps
        // transparency, which the plain bitmap format flattens onto black.
        if (data.GetData(PngFormat) is MemoryStream png)
        {
            try
            {
                using (png)
                {
                    var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
                }
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
            {
                // Claimed to be a PNG and was not. Fall through to the bitmap.
            }
        }

        return data.GetData(DataFormats.Bitmap) as BitmapSource;
    }

    private IReadOnlyList<string> SaveImage(BitmapSource image)
    {
        var folder = _staging.CreateBatchFolder();
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        var destination = StagingArea.SafePathFor(folder, $"Image {stamp}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var output = File.Create(destination);
        encoder.Save(output);

        return [destination];
    }

    private static string? TryReadUrl(IDataObject data)
    {
        if (data.GetData(UrlFormat) is MemoryStream stream)
        {
            using (stream)
            {
                // Written by the source as UTF-16 with a terminating null, which
                // would otherwise end up inside the string.
                var text = Encoding.Unicode.GetString(stream.ToArray()).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private IReadOnlyList<string> SaveUrl(string url)
    {
        var folder = _staging.CreateBatchFolder();

        var name = "Link";
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            // The host makes a far more recognisable tile than "Link" does.
            name = parsed.Host.Length > 0 ? parsed.Host : name;
        }

        var destination = StagingArea.SafePathFor(folder, name + ".url");

        // The .url format is a plain INI file that Explorer treats as a shortcut,
        // which means the tile gets the site's favicon and opens in the browser.
        File.WriteAllText(destination, $"[InternetShortcut]{Environment.NewLine}URL={url}{Environment.NewLine}");

        return [destination];
    }

    private IReadOnlyList<string> SaveText(string text)
    {
        var folder = _staging.CreateBatchFolder();

        // The first line makes a better name than a timestamp, so long as it is
        // short enough to read on a tile.
        var firstLine = text.Split('\n', '\r')[0].Trim();
        var name = firstLine.Length switch
        {
            0 => "Text",
            > 40 => firstLine[..40],
            _ => firstLine,
        };

        var destination = StagingArea.SafePathFor(folder, name + ".txt");
        File.WriteAllText(destination, text);

        return [destination];
    }
}
