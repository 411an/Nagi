using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;

namespace Nagi.Core.Services.Implementations;

/// <inheritdoc />
public sealed class TrackSpectrumService : ITrackSpectrumService, IDisposable
{
    private readonly IFFmpegService _ffmpegService;
    private readonly ILogger<TrackSpectrumService> _logger;
    private readonly IPcmExtractor _pcmExtractor;
    private readonly object _lock = new();

    private Entry? _current;

    public TrackSpectrumService(IPcmExtractor pcmExtractor, IFFmpegService ffmpegService,
        ILogger<TrackSpectrumService> logger)
    {
        _pcmExtractor = pcmExtractor ?? throw new ArgumentNullException(nameof(pcmExtractor));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TrackSpectrum Request(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        var key = new SongKey(song.Id, song.FilePath, song.StartOffsetTicks);

        lock (_lock)
        {
            if (_current is { } existing && existing.Key == key) return existing.Spectrum;

            _current?.Cancellation.Cancel();

            var spectrum = new TrackSpectrum(SpectrumAnalyzer.BandCount, SpectrumAnalyzer.FrameDuration, song.Duration);
            var cancellation = new CancellationTokenSource();
            _current = new Entry(key, spectrum, cancellation);
            var token = cancellation.Token;
            _ = Task.Run(() => AnalyzeAsync(song, spectrum, token), token);
            return spectrum;
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            if (_current is null) return;
            _current.Cancellation.Cancel();
            // A finished spectrum stays usable; an unfinished one starts over next time.
            if (_current.Spectrum.Status != TrackSpectrumStatus.Complete) _current = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _current?.Cancellation.Cancel();
            _current = null;
        }
    }

    private async Task AnalyzeAsync(Song song, TrackSpectrum spectrum, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrEmpty(song.FilePath) || !await _ffmpegService.IsFFmpegInstalledAsync().ConfigureAwait(false))
            {
                spectrum.MarkUnavailable();
                return;
            }

            var options = new PcmExtractionOptions(
                SpectrumAnalyzer.SampleRate,
                1,
                song.IsCueTrack ? TimeSpan.FromTicks(song.StartOffsetTicks) : null,
                song.IsCueTrack ? song.Duration : null,
                LowPriority: true);

            var analyzer = new SpectrumAnalyzer();
            await foreach (var chunk in _pcmExtractor.ExtractStreamingAsync(song.FilePath, options, token)
                               .ConfigureAwait(false))
            {
                analyzer.Process(chunk.Span, spectrum.Append);
            }

            token.ThrowIfCancellationRequested();
            analyzer.Flush(spectrum.Append);

            if (spectrum.FrameCount > 0)
                spectrum.MarkComplete();
            else
                spectrum.MarkUnavailable();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another track was requested or the visualizer was hidden.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not analyze the spectrum of {FilePath}", song.FilePath);
            spectrum.MarkUnavailable();
        }
    }

    private readonly record struct SongKey(Guid Id, string FilePath, long StartOffsetTicks);

    // The token source is cancelled but never disposed: the analysis may still be registering
    // callbacks on its token while it winds down, and the source owns no timer.
    private sealed record Entry(SongKey Key, TrackSpectrum Spectrum, CancellationTokenSource Cancellation);
}
