namespace Nagi.Core.Models;

public enum TrackSpectrumStatus
{
    /// <summary>Frames are still arriving; the start of the track is usually ready.</summary>
    Analyzing,

    /// <summary>The whole track has been analyzed.</summary>
    Complete,

    /// <summary>No spectrum can be made, for example because FFmpeg is missing.</summary>
    Unavailable
}

/// <summary>
///     Bar levels of one track, filled in while the track is decoded in the background and kept
///     only in memory: 28 bytes per frame, about 150 KB for four minutes.
/// </summary>
public sealed class TrackSpectrum
{
    private readonly object _lock = new();
    private byte[] _frames;
    private int _frameCount;
    private TrackSpectrumStatus _status = TrackSpectrumStatus.Analyzing;

    public TrackSpectrum(int bandCount, TimeSpan frameDuration, TimeSpan expectedLength)
    {
        if (bandCount <= 0) throw new ArgumentOutOfRangeException(nameof(bandCount));
        if (frameDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(frameDuration));
        BandCount = bandCount;
        FrameDuration = frameDuration;
        var expectedFrames = expectedLength > TimeSpan.Zero
            ? (int)Math.Min(expectedLength / frameDuration + 16, 4_000_000)
            : 1024;
        _frames = new byte[expectedFrames * bandCount];
    }

    public int BandCount { get; }

    public TimeSpan FrameDuration { get; }

    public TrackSpectrumStatus Status
    {
        get { lock (_lock) return _status; }
    }

    public int FrameCount
    {
        get { lock (_lock) return _frameCount; }
    }

    public void Append(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != BandCount) throw new ArgumentException("Frame size does not match the band count.", nameof(frame));
        lock (_lock)
        {
            var needed = (_frameCount + 1) * BandCount;
            if (needed > _frames.Length) Array.Resize(ref _frames, Math.Max(needed, _frames.Length * 2));
            frame.CopyTo(_frames.AsSpan(_frameCount * BandCount));
            _frameCount++;
        }
    }

    public void MarkComplete()
    {
        lock (_lock)
        {
            if (_status == TrackSpectrumStatus.Analyzing) _status = TrackSpectrumStatus.Complete;
        }
    }

    public void MarkUnavailable()
    {
        lock (_lock) _status = TrackSpectrumStatus.Unavailable;
    }

    /// <summary>
    ///     Writes the bar levels (0 to 1) at <paramref name="position" />, blending the two nearest
    ///     frames. Returns false when that part of the track has not been analyzed yet.
    /// </summary>
    public bool TryGetLevels(TimeSpan position, Span<float> levels)
    {
        if (levels.Length < BandCount) throw new ArgumentException("Too few levels.", nameof(levels));
        var exact = Math.Max(0, position / FrameDuration);
        var index = (int)exact;
        var blend = (float)(exact - index);

        lock (_lock)
        {
            if (index >= _frameCount) return false;
            var next = Math.Min(index + 1, _frameCount - 1);
            var a = _frames.AsSpan(index * BandCount, BandCount);
            var b = _frames.AsSpan(next * BandCount, BandCount);
            for (var i = 0; i < BandCount; i++) levels[i] = (a[i] + (b[i] - a[i]) * blend) / 255f;
            return true;
        }
    }
}
