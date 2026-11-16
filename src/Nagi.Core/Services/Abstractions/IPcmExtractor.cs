namespace Nagi.Core.Services.Abstractions;

/// <summary>
///     Represents a chunk of PCM audio samples for streaming extraction.
/// </summary>
/// <param name="Samples">Interleaved float samples normalized to [-1, 1]. In streaming scenarios, this may be a rented buffer larger than the actual data.</param>
/// <param name="Length">The number of valid samples in the buffer.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="Channels">Number of audio channels.</param>
public readonly record struct AudioChunk(float[] Samples, int Length, int SampleRate, int Channels)
{
    public ReadOnlySpan<float> Span => Samples.AsSpan(0, Length);
}

/// <summary>
///     How to decode a file: the output format, an optional part of the file, and whether the
///     decoder should give way to everything else running on the computer.
/// </summary>
/// <param name="SampleRate">Output sample rate in Hz.</param>
/// <param name="Channels">Output channel count; 1 mixes the file down to mono.</param>
/// <param name="Start">Where to start decoding, for a track inside a CUE image.</param>
/// <param name="Duration">How much to decode from <paramref name="Start" />.</param>
/// <param name="LowPriority">Run the decoder below normal priority.</param>
public sealed record PcmExtractionOptions(
    int SampleRate,
    int Channels,
    TimeSpan? Start = null,
    TimeSpan? Duration = null,
    bool LowPriority = false);

/// <summary>
///     Defines a service for extracting raw PCM audio samples from audio files.
///     Used for ReplayGain calculation and the spectrum visualizer.
/// </summary>
public interface IPcmExtractor
{
    /// <summary>
    ///     Extracts PCM samples from an audio file.
    /// </summary>
    /// <param name="filePath">Path to the audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    ///     Tuple containing interleaved float samples (normalized to [-1, 1]), sample rate, and channel count.
    ///     Returns null if extraction failed.
    /// </returns>
    Task<(float[] Samples, int SampleRate, int Channels)?> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Extracts PCM samples from an audio file in a streaming fashion.
    ///     This method yields chunks of audio data as they are decoded, reducing memory usage.
    /// </summary>
    /// <param name="filePath">Path to the audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of audio chunks.</returns>
    IAsyncEnumerable<AudioChunk> ExtractStreamingAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Streams PCM samples in the requested format, optionally for one part of the file.
    /// </summary>
    /// <param name="filePath">Path to the audio file.</param>
    /// <param name="options">Output format, range and priority.</param>
    /// <param name="cancellationToken">Cancellation token; cancelling stops the decoder.</param>
    IAsyncEnumerable<AudioChunk> ExtractStreamingAsync(
        string filePath,
        PcmExtractionOptions options,
        CancellationToken cancellationToken = default);
}
