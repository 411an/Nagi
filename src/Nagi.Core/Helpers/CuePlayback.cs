using System.Globalization;
using Nagi.Core.Models;

namespace Nagi.Core.Helpers;

/// <summary>Configures CUE ranges and keeps LibVLC 4's track-local timeline in bounds.</summary>
public static class CuePlayback
{
    // LibVLC 4 applies start-time internally to both seeking and its playback
    // clock. Do not add or subtract the file offset a second time.
    public static TimeSpan ToSeekPosition(Song song, TimeSpan position)
    {
        position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (!song.IsCueTrack) return position;
        return position > song.Duration ? song.Duration : position;
    }

    public static TimeSpan ToTrackPosition(Song song, TimeSpan position) => ToSeekPosition(song, position);

    public static IEnumerable<string> GetMediaOptions(Song song)
    {
        if (!song.IsCueTrack) yield break;
        var start = TimeSpan.FromTicks(song.StartOffsetTicks).TotalSeconds;
        yield return ":start-time=" + start.ToString("0.#######", CultureInfo.InvariantCulture);
        yield return ":stop-time=" + (start + song.Duration.TotalSeconds).ToString("0.#######", CultureInfo.InvariantCulture);
    }
}
