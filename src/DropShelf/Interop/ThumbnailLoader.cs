using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DropShelf.Interop;

/// <summary>
/// Loads shell thumbnails off the UI thread and hands them back on it.
/// </summary>
/// <remarks>
/// A thumbnail can take a noticeable moment to produce. Video files are decoded,
/// files on a network share are fetched, and third party preview handlers can do
/// anything at all. Doing that work on the UI thread would freeze the shelf for
/// as long as it takes, so it happens on one dedicated worker instead.
/// <para>
/// One worker rather than a pool, for two reasons. Shell COM needs a single
/// threaded apartment, which the thread pool does not provide, and a queue of one
/// keeps the drop order intact so tiles fill in left to right.
/// </para>
/// </remarks>
public sealed class ThumbnailLoader : IDisposable
{
    private readonly BlockingCollection<PendingRequest> _queue = new();
    private readonly ConcurrentDictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dispatcher _uiDispatcher;
    private readonly Thread _worker;
    private bool _disposed;

    private readonly record struct PendingRequest(string Path, int PixelSize, Action<BitmapSource?> Completed);

    public ThumbnailLoader()
    {
        // Captured here, which means construction has to happen on the UI thread.
        _uiDispatcher = Dispatcher.CurrentDispatcher;

        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "DropShelf thumbnails",
        };

        // Without this the shell calls fail. COM apartment state has to be set
        // before the thread starts and cannot be changed afterwards.
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>
    /// Requests an image for a path. The callback runs on the UI thread, either
    /// immediately from cache or later once the shell has produced one.
    /// </summary>
    public void Request(string path, int pixelSize, Action<BitmapSource?> completed)
    {
        var key = CacheKey(path, pixelSize);
        if (_cache.TryGetValue(key, out var cached))
        {
            completed(cached);
            return;
        }

        if (_disposed)
        {
            return;
        }

        try
        {
            _queue.Add(new PendingRequest(path, pixelSize, completed));
        }
        catch (InvalidOperationException)
        {
            // The queue was completed by Dispose while this call was in flight.
        }
    }

    private void Run()
    {
        foreach (var request in _queue.GetConsumingEnumerable())
        {
            BitmapSource? image = null;

            try
            {
                image = ShellImage.TryLoad(request.Path, request.PixelSize);
            }
            catch (Exception)
            {
                // Thumbnail handlers are third party code running in this
                // process. A broken one must not be allowed to take the worker
                // thread down with it, because that would silently stop every
                // later thumbnail from ever loading.
            }

            if (image is not null)
            {
                _cache[CacheKey(request.Path, request.PixelSize)] = image;
            }

            var completed = request.Completed;
            var result = image;
            _uiDispatcher.BeginInvoke(() => completed(result));
        }
    }

    private static string CacheKey(string path, int pixelSize) => $"{pixelSize}|{path}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Ends the foreach in Run once the queue drains, which lets the worker
        // finish naturally rather than being aborted mid COM call.
        _queue.CompleteAdding();
    }
}
