using Nagi.Core.Models;

namespace Nagi.Core.Services.Abstractions;

/// <summary>
///     Supplies the real spectrum of the playing track to the visualizer. Nothing is stored:
///     the track is decoded in the background when it is requested, and analysis of the
///     previous track is cancelled.
/// </summary>
public interface ITrackSpectrumService
{
    /// <summary>
    ///     Returns the spectrum of <paramref name="song" />, starting its analysis on the first
    ///     call. Asking for the same song again returns the same object.
    /// </summary>
    TrackSpectrum Request(Song song);

    /// <summary>Cancels the running analysis, for example when the visualizer is hidden.</summary>
    void Cancel();
}
