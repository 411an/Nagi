using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ATL.CatalogDataReaders.BinaryLogic;
using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;

namespace Nagi.Core.Helpers;

/// <summary>
/// Reads external, single-file album CUE sheets with ATL. ATL supplies track tags;
/// INDEX 01 is read separately because ATL exposes durations but not absolute offsets.
/// </summary>
internal static class CueSheetReader
{
    // ATL 7.16.0 uses a shared non-thread-safe encoding cache while reading CUE files.
    private static readonly object AtlCueReadLock = new();
    private static readonly Regex FileLine = new("^FILE\\s+(?:\"(?<path>[^\"]+)\"|(?<path>.+?))\\s+\\S+\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TrackLine = new("^TRACK\\s+(?<number>\\d+)\\s+(?<type>\\S+)\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex IndexLine = new("^INDEX\\s+01\\s+(?<minutes>\\d+):(?<seconds>\\d+):(?<frames>\\d+)\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static CueSheetReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static async Task ExpandAsync(SongFileMetadata metadata, IFileSystemService fileSystem, ILogger logger)
    {
        var directory = Path.GetDirectoryName(metadata.FilePath);
        if (string.IsNullOrEmpty(directory)) return;

        try
        {
            var candidates = fileSystem.GetFiles(directory, "*.cue") ?? [];
            foreach (var cuePath in candidates.OrderByDescending(p =>
                         Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(metadata.FilePath), StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var bytes = await fileSystem.ReadAllBytesAsync(cuePath).ConfigureAwait(false);
                    if (bytes is not { Length: > 0 }) continue;
                    var text = CueTextEncoding.Decode(bytes);
                    var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
                    var files = lines.Select(l => FileLine.Match(l)).Where(m => m.Success).ToArray();
                    if (!files.Any(m => Path.GetFullPath(Path.Combine(directory, m.Groups["path"].Value))
                            .Equals(Path.GetFullPath(metadata.FilePath), StringComparison.OrdinalIgnoreCase))) continue;

                    if (files.Length != 1)
                        throw new NotSupportedException("Only single-audio-file album CUE sheets are supported.");

                    var offsets = ReadOffsets(lines, metadata.Duration);
                    var cue = await ReadWithAtlAsync(lines, metadata.FilePath).ConfigureAwait(false);
                    if (cue.Tracks.Count != offsets.Count)
                        throw new InvalidDataException("ATL and the CUE track index disagree.");

                    var cueModified = fileSystem.GetLastWriteTimeUtc(cuePath);
                    var expanded = new List<SongFileMetadata>(offsets.Count);
                    for (var i = 0; i < offsets.Count; i++)
                    {
                        var (number, start) = offsets[i];
                        var tags = cue.Tracks[i];
                        if (tags.TrackNumber != number)
                            throw new InvalidDataException("ATL and the CUE track numbers disagree.");
                        var end = i + 1 < offsets.Count ? offsets[i + 1].Start : metadata.Duration;
                        var track = metadata.CloneForCueTrack();
                        track.CueTrackNumber = number;
                        track.TrackNumber = number;
                        track.TrackCount = offsets.Count;
                        track.StartOffsetTicks = start.Ticks;
                        track.Duration = end - start;
                        track.CueFilePath = cuePath;
                        track.CueFileModifiedDate = cueModified;
                        track.Title = Clean(tags.Title) ?? $"{metadata.Title} ({number})";
                        track.Album = Clean(cue.Title) ?? metadata.Album;
                        track.AlbumArtists = Clean(cue.Artist) is { } albumArtist ? [albumArtist] : metadata.AlbumArtists;
                        track.Artists = Clean(tags.Artist) is { } artist ? [artist] : track.AlbumArtists;
                        track.Lyrics = null;
                        track.LrcFilePath = null;
                        track.MusicBrainzTrackId = null;
                        track.CueTracks = [];
                        track.CueReadFailed = false;
                        expanded.Add(track);
                    }

                    metadata.CueTracks = expanded;
                    metadata.CueReadFailed = false;
                    logger.LogInformation("Read {TrackCount} CUE tracks for {FilePath} from {CuePath}.", expanded.Count, metadata.FilePath, cuePath);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    metadata.CueReadFailed = true;
                    logger.LogWarning(ex, "Could not read CUE sheet {CuePath} for {FilePath}.", cuePath, metadata.FilePath);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metadata.CueReadFailed = true;
            logger.LogWarning(ex, "Could not discover CUE sheets for {FilePath}.", metadata.FilePath);
        }
    }

    private static string? Clean(string? value) => ArtistNameHelper.NormalizeStringCore(value);

    private static async Task<Cue> ReadWithAtlAsync(IEnumerable<string> lines, string audioPath)
    {
        // ATL's CUE API accepts only a path and guesses its own encoding. Feed it a
        // UTF-8 copy so corrected legacy encodings and tab-separated directives are
        // interpreted consistently. The source CUE and audio are never rewritten.
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"nagi-{Guid.NewGuid():N}.cue");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                foreach (var line in lines)
                {
                    var normalized = FileLine.IsMatch(line)
                        ? $"FILE \"{Path.GetFullPath(audioPath)}\" WAVE"
                        : Regex.Replace(line, @"^(\S+)\s+", "$1 ");
                    if (TrackLine.IsMatch(line) || normalized.StartsWith("INDEX ", StringComparison.OrdinalIgnoreCase))
                        normalized = Regex.Replace(normalized, @"\s+", " ");
                    await writer.WriteLineAsync(normalized).ConfigureAwait(false);
                }
            }

            lock (AtlCueReadLock)
            {
                return new Cue(temporaryPath);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static List<(int Number, TimeSpan Start)> ReadOffsets(IEnumerable<string> lines, TimeSpan duration)
    {
        var result = new List<(int Number, TimeSpan Start)>();
        var numbers = new HashSet<int>();
        int? current = null;
        var hasIndex = false;
        foreach (var line in lines)
        {
            var track = TrackLine.Match(line);
            if (track.Success)
            {
                if (current.HasValue && !hasIndex) throw new InvalidDataException("CUE track has no INDEX 01.");
                var number = int.Parse(track.Groups["number"].Value, CultureInfo.InvariantCulture);
                if (number is < 1 or > 99 || !numbers.Add(number) || !track.Groups["type"].Value.Equals("AUDIO", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid or duplicate audio track number in CUE.");
                current = number;
                hasIndex = false;
                continue;
            }

            var index = IndexLine.Match(line);
            if (!index.Success) continue;
            if (!current.HasValue || hasIndex) throw new InvalidDataException("Unexpected INDEX 01 in CUE.");
            var minutes = long.Parse(index.Groups["minutes"].Value, CultureInfo.InvariantCulture);
            var seconds = int.Parse(index.Groups["seconds"].Value, CultureInfo.InvariantCulture);
            var frames = int.Parse(index.Groups["frames"].Value, CultureInfo.InvariantCulture);
            if (seconds >= 60 || frames >= 75) throw new InvalidDataException("Invalid CUE timecode.");
            var ticks = checked((minutes * 60 + seconds) * TimeSpan.TicksPerSecond + frames * TimeSpan.TicksPerSecond / 75);
            var start = TimeSpan.FromTicks(ticks);
            if (start >= duration || (result.Count > 0 && start <= result[^1].Start))
                throw new InvalidDataException("CUE indices must increase within the audio file.");
            result.Add((current.Value, start));
            hasIndex = true;
        }
        if (result.Count == 0 || !hasIndex) throw new InvalidDataException("CUE has no complete audio tracks.");
        return result;
    }
}
