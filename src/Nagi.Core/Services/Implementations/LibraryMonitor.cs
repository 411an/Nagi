using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nagi.Core.Helpers;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Data;

namespace Nagi.Core.Services.Implementations;

/// <summary>Watches registered roots and queues incremental scans after file activity settles.</summary>
public sealed class LibraryMonitor : IAsyncDisposable
{
    private readonly ILibraryService _library;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<LibraryMonitor> _logger;
    private readonly Func<string, Action, Action<Exception>, IDisposable> _createWatcher;
    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _watcherRetryInterval;
    private readonly ConcurrentDictionary<Guid, long> _pending = new();
    private readonly ConcurrentDictionary<Guid, byte> _faulted = new();
    private readonly Dictionary<Guid, (string Path, IDisposable Watcher)> _watchers = new();
    private readonly Dictionary<Guid, string> _roots = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifecycleLock = new();
    private Task? _worker;
    private int _rootsDirty = 1;
    private int _stopping;
    private bool _disposed;

    public LibraryMonitor(ILibraryService library, IFileSystemService fileSystem, ILogger<LibraryMonitor> logger)
        : this(library, fileSystem, logger, (path, changed, error) => new LibraryFolderWatcher(path, changed, error),
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30))
    {
    }

    internal LibraryMonitor(ILibraryService library, IFileSystemService fileSystem, ILogger<LibraryMonitor> logger,
        Func<string, Action, Action<Exception>, IDisposable> createWatcher,
        TimeSpan quietPeriod, TimeSpan pollInterval, TimeSpan watcherRetryInterval)
    {
        _library = library;
        _fileSystem = fileSystem;
        _logger = logger;
        _createWatcher = createWatcher;
        _quietPeriod = quietPeriod;
        _pollInterval = pollInterval;
        _watcherRetryInterval = watcherRetryInterval;
        _library.LibraryContentChanged += OnLibraryContentChanged;
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Volatile.Read(ref _stopping) != 0) return;
            _worker ??= Task.Run(RunAsync);
        }
    }

    private void OnLibraryContentChanged(object? sender, LibraryContentChangedEventArgs e)
    {
        if (e.ChangeType is LibraryChangeType.FolderAdded or LibraryChangeType.FolderRemoved or LibraryChangeType.LibraryRescanned)
            Interlocked.Exchange(ref _rootsDirty, 1);
    }

    private void RequestScan(Guid rootId)
    {
        if (Volatile.Read(ref _stopping) == 0)
            _pending[rootId] = Environment.TickCount64;
    }

    private void OnWatcherError(Guid rootId, Exception exception)
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        _logger.LogWarning(exception, "Reconnecting library folder watcher for {FolderId}.", rootId);
        _faulted[rootId] = 0;
        Interlocked.Exchange(ref _rootsDirty, 1);
        RequestScan(rootId);
    }

    private async Task RunAsync()
    {
        var token = _shutdown.Token;
        var lastRootRefresh = Environment.TickCount64 - (long)_watcherRetryInterval.TotalMilliseconds;
        using var timer = new PeriodicTimer(_pollInterval);
        try
        {
            do
            {
                try
                {
                    if (Interlocked.Exchange(ref _rootsDirty, 0) != 0 ||
                        Environment.TickCount64 - lastRootRefresh >= _watcherRetryInterval.TotalMilliseconds)
                    {
                        await RefreshWatchersAsync(token).ConfigureAwait(false);
                        lastRootRefresh = Environment.TickCount64;
                    }

                    foreach (var pending in _pending.ToArray())
                    {
                        token.ThrowIfCancellationRequested();
                        if (Environment.TickCount64 - pending.Value < _quietPeriod.TotalMilliseconds) continue;
                        // Remove only the observed event; a newer change needs its own scan.
                        if (!((ICollection<KeyValuePair<Guid, long>>)_pending).Remove(pending)) continue;
                        if (!_roots.ContainsKey(pending.Key)) continue;

                        try
                        {
                            await _library.RefreshFolderInBackgroundAsync(pending.Key, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Will retry automatic refresh of folder {FolderId}.", pending.Key);
                            RequestScan(pending.Key);
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Automatic library refresh failed; folder watchers will be checked again.");
                    Interlocked.Exchange(ref _rootsDirty, 1);
                }
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var registration in _watchers.Values) registration.Watcher.Dispose();
            _watchers.Clear();
            _pending.Clear();
        }
    }

    private async Task RefreshWatchersAsync(CancellationToken token)
    {
        var roots = (await _library.GetRootFoldersAsync(token).ConfigureAwait(false)).ToDictionary(folder => folder.Id);
        foreach (var id in _roots.Keys.Where(id => !roots.ContainsKey(id)).ToArray())
        {
            if (_watchers.Remove(id, out var removed)) removed.Watcher.Dispose();
            _roots.Remove(id);
            _pending.TryRemove(id, out _);
            _faulted.TryRemove(id, out _);
        }

        foreach (var root in roots.Values)
        {
            token.ThrowIfCancellationRequested();
            _roots[root.Id] = root.Path;
            var available = _fileSystem.DirectoryExists(root.Path);
            var faulted = _faulted.TryRemove(root.Id, out _);
            if (_watchers.TryGetValue(root.Id, out var registration) &&
                (!available || faulted || !string.Equals(root.Path, registration.Path, StringComparison.OrdinalIgnoreCase)))
            {
                registration.Watcher.Dispose();
                _watchers.Remove(root.Id);
            }

            if (!available)
            {
                // Disconnected drives stay in the library and are retried on the next check.
                _pending.TryRemove(root.Id, out _);
                continue;
            }
            if (_watchers.ContainsKey(root.Id)) continue;

            try
            {
                var watcher = _createWatcher(root.Path, () => RequestScan(root.Id), ex => OnWatcherError(root.Id, ex));
                _watchers.Add(root.Id, (root.Path, watcher));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not watch {FolderPath}; incremental scans will be retried periodically.", root.Path);
            }

            // Catch additions made while the app was closed or the drive was disconnected.
            _pending.TryAdd(root.Id, Environment.TickCount64 - (long)_quietPeriod.TotalMilliseconds);
        }
    }

    public async Task StopAsync()
    {
        Task? worker;
        lock (_lifecycleLock)
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 0)
            {
                _library.LibraryContentChanged -= OnLibraryContentChanged;
                _shutdown.Cancel();
            }
            worker = _worker;
        }
        if (worker is not null) await worker.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            _shutdown.Dispose();
        }
    }
}
