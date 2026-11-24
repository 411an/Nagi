using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;

namespace Nagi.Core.Services.Implementations;

/// <summary>
/// Owns the lifetime of one library listen session independently of the audio path.
/// </summary>
internal sealed class PlaybackListenSession
{
    private readonly ILibraryService _library;
    private readonly ILogger _logger;
    private readonly Task<long?> _started;
    private readonly object _endLock = new();
    private Task? _endTask;

    public PlaybackListenSession(ILibraryService library, Guid songId, PlaybackContext context, ILogger logger)
    {
        _library = library;
        _logger = logger;
        StartedUtc = DateTime.UtcNow;
        // SQLite's async API can execute synchronous work before returning a Task.
        // Keep even that part away from the playback/dispatcher path.
        _started = Task.Run(() => StartAsync(songId, context));
    }

    public DateTime StartedUtc { get; }

    public Task<long?> Started => _started;

    public long? Id => _started.Status == TaskStatus.RanToCompletion ? _started.GetAwaiter().GetResult() : null;

    public Task EndAsync(TimeSpan position, PlaybackEndReason reason)
    {
        lock (_endLock)
        {
            return _endTask ??= EndCoreAsync(position, reason);
        }
    }

    private async Task<long?> StartAsync(Guid songId, PlaybackContext context)
    {
        try
        {
            return await _library.StartListenSessionAsync(songId, context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start playback listen session for song {SongId}.", songId);
            return null;
        }
    }

    private async Task EndCoreAsync(TimeSpan position, PlaybackEndReason reason)
    {
        var id = await _started.ConfigureAwait(false);
        if (!id.HasValue)
            return;

        try
        {
            await _library.FinalizeListenSessionAsync(id.Value, position, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize playback listen session {ListenHistoryId}.", id.Value);
        }
    }
}
