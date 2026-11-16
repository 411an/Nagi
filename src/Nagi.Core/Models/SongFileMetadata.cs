namespace Nagi.Core.Models;

/// <summary>
///     A data transfer object representing metadata extracted from a single audio file.
/// </summary>
public class SongFileMetadata
{
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    ///     Cue track identity within an external CUE file; 0 represents the whole file and positive values identify a track.
    /// </summary>
    public int CueTrackNumber { get; set; }

    /// <summary>
    ///     Absolute start position of this track in the physical audio file, in ticks; 0 is the beginning.
    /// </summary>
    public long StartOffsetTicks { get; set; }

    public string? CueFilePath { get; set; }
    public DateTime? CueFileModifiedDate { get; set; }
    public IReadOnlyList<SongFileMetadata> CueTracks { get; set; } = [];
    public bool CueReadFailed { get; set; }

    internal SongFileMetadata CloneForCueTrack() => (SongFileMetadata)MemberwiseClone();

    public string Title { get; set; } = string.Empty;
    public List<string> Artists { get; set; } = new();
    public string? Album { get; set; } = string.Empty;
    public List<string> AlbumArtists { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public string? CoverArtUri { get; set; }
    public string? LightSwatchId { get; set; }
    public string? DarkSwatchId { get; set; }
    public int? Year { get; set; }
    public List<string> Genres { get; set; } = new();
    public int? TrackNumber { get; set; }
    public int? TrackCount { get; set; }
    public int? DiscNumber { get; set; }
    public int? DiscCount { get; set; }
    public int? SampleRate { get; set; }
    public int? Bitrate { get; set; }
    public int? Channels { get; set; }
    public DateTime? FileCreatedDate { get; set; }
    public DateTime? FileModifiedDate { get; set; }
    public string? Lyrics { get; set; }
    public string? LrcFilePath { get; set; }
    public double? Bpm { get; set; }
    public double? ReplayGainTrackGain { get; set; }
    public double? ReplayGainTrackPeak { get; set; }
    public string? Composer { get; set; }
    public bool ExtractionFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Grouping { get; set; }
    public string? Copyright { get; set; }
    public string? Comment { get; set; }
    public string? Conductor { get; set; }
    public string? MusicBrainzTrackId { get; set; }
    public string? MusicBrainzReleaseId { get; set; }
}
