using Microsoft.Extensions.Logging;
using Nagi.Core.Services.Abstractions;

namespace Nagi.Core.Services.Implementations;

/// <summary>Performs one best-effort refresh of the available library roots at startup.</summary>
public sealed class LibraryStartupRefresh : IAsyncDisposable
{
    private readonly ILibraryService _library;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<LibraryStartupRefresh> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifecycleLock = new();
    private Task? _work;
    private bool _stopping;
    private bool _disposed;

    public LibraryStartupRefresh(
        ILibraryService library,
        IFileSystemService fileSystem,
        ILogger<LibraryStartupRefresh> logger)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts the single startup pass. Repeated calls share the same pass.</summary>
    public Task StartAsync()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping) return Task.CompletedTask;
            return _work ??= Task.Run(() => RunAsync(_shutdown.Token));
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting one-time library startup refresh.");
        try
        {
            var roots = await _library.GetRootFoldersAsync(cancellationToken).ConfigureAwait(false);
            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!_fileSystem.DirectoryExists(root.Path))
                    {
                        _logger.LogDebug("Skipping unavailable library root {FolderPath}.", root.Path);
                        continue;
                    }

                    await _library.RefreshFolderInBackgroundAsync(root.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Startup refresh failed for library root {FolderId}; continuing.", root.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Library startup refresh was cancelled.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Library startup refresh failed.");
        }
        finally
        {
            _logger.LogInformation("Library startup refresh finished.");
        }
    }

    public async Task StopAsync()
    {
        Task? work;
        lock (_lifecycleLock)
        {
            if (!_stopping)
            {
                _stopping = true;
                _shutdown.Cancel();
            }
            work = _work;
        }

        if (work is not null) await work.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
