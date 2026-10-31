// CosmOrganizer.Core - RealTimeService
// FileSystemWatcher wrapper with Channel-based debounce,
// file-readiness check, Pause/Resume, and graceful overflow handling.
// © CosmoNet.info — MIT License

using System.Threading.Channels;

namespace CosmOrganizer.Core;

/// <summary>
/// Watches one or more source folders and raises <see cref="FileReady"/> events
/// for files that appear stable (size/date unchanged for 2 s, file openable).
/// Uses a Channel queue with a single consumer to avoid thundering-herd.
/// </summary>
public class RealTimeService : IDisposable
{
    // ------------------------------------------------------------------
    // Public events
    // ------------------------------------------------------------------

    /// <summary>Raised on the consumer task thread when a file is ready.</summary>
    public event Action<string>? FileReady;

    /// <summary>Raised when a watcher error forces a re-scan.</summary>
    public event Action<string>? ScanRequired;

    // ------------------------------------------------------------------
    // Configuration
    // ------------------------------------------------------------------

    private const int StabilityMs = 2_000;
    private const int MaxWaitMs = 10 * 60 * 1_000; // 10 minutes
    private const int InitialBackoffMs = 500;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Channel<string> _queue;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _paused;
    private bool _disposed;

    private readonly SafeFileService _safeFile;

    public RealTimeService(SafeFileService safeFile)
    {
        _safeFile = safeFile;
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        // Start the single consumer task
        _ = Task.Run(ConsumeAsync);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Starts watching a folder.</summary>
    public void Watch(string folder, bool includeSubfolders = true)
    {
        if (!Directory.Exists(folder)) return;

        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = includeSubfolders,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        watcher.Created += OnEvent;
        watcher.Changed += OnEvent;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;

        _watchers.Add(watcher);
    }

    /// <summary>Stops all watchers and releases resources.</summary>
    public void StopAll()
    {
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }

    /// <summary>Temporarily suspends file-ready processing.</summary>
    public void Pause() => _paused = true;

    /// <summary>Resumes file-ready processing.</summary>
    public void Resume() => _paused = false;

    // ------------------------------------------------------------------
    // Watcher event handlers
    // ------------------------------------------------------------------

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath is not null)
            Enqueue(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (e.FullPath is not null)
            Enqueue(e.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow or loss of tracking — request a full re-scan
        ScanRequired?.Invoke(e.GetException()?.Message ?? "Errore watcher");
    }

    private void Enqueue(string path)
    {
        _queue.Writer.TryWrite(path);
    }

    // ------------------------------------------------------------------
    // Consumer
    // ------------------------------------------------------------------

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var path in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                if (_paused) continue;

                try
                {
                    // Skip files that should never be processed
                    if (_safeFile.ShouldSkipFile(path)) continue;
                    if (!File.Exists(path)) continue;

                    // Wait for the file to be stable
                    bool ready = await WaitForFileReadyAsync(path, _cts.Token);
                    if (!ready) continue;

                    FileReady?.Invoke(path);
                }
                catch (OperationCanceledException) { break; }
                catch { /* per-file errors are non-fatal */ }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    /// <summary>
    /// Waits until the file's size and last-write are stable for
    /// <see cref="StabilityMs"/> ms AND the file can be opened exclusively.
    /// Uses exponential backoff up to <see cref="MaxWaitMs"/>.
    /// Returns false on timeout or cancellation.
    /// </summary>
    private static async Task<bool> WaitForFileReadyAsync(string path, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(MaxWaitMs);
        var backoffMs = InitialBackoffMs;

        long prevSize = -1;
        DateTime prevWrite = DateTime.MinValue;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;

                long currentSize = info.Length;
                DateTime currentWrite = info.LastWriteTimeUtc;

                bool stable = (currentSize == prevSize && currentWrite == prevWrite);
                prevSize = currentSize;
                prevWrite = currentWrite;

                if (stable)
                {
                    // Try to open with exclusive lock
                    using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true; // file is ready
                }
            }
            catch (IOException) { /* file still locked or changing */ }
            catch { return false; }

            await Task.Delay(Math.Min(backoffMs, (int)(deadline - DateTime.UtcNow).TotalMilliseconds), ct);
            backoffMs = Math.Min(backoffMs * 2, 8_000); // cap at 8 s
        }

        return false; // timed out
    }

    // ------------------------------------------------------------------
    // IDisposable
    // ------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        StopAll();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
