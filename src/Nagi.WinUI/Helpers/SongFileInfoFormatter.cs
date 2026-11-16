using System.Text;
using Nagi.Core.Models;
using Nagi.WinUI.Resources;

namespace Nagi.WinUI.Helpers;

public static class SongFileInfoFormatter
{
    public static string Build(Song song)
    {
        var genre = song.Genres?.FirstOrDefault()?.Name;
        var duration = song.Duration.TotalHours >= 1
            ? song.Duration.ToString(@"h\:mm\:ss")
            : song.Duration.ToString(@"m\:ss");

        var sb = new StringBuilder();
        sb.AppendLine($"{Strings.Shell_MetaTitle}: {song.Title}");
        sb.AppendLine($"{Strings.Shell_MetaArtist}: {song.ArtistName}");
        sb.AppendLine($"{Strings.Shell_MetaAlbum}: {song.Album?.Title ?? string.Empty}");
        if (!string.IsNullOrWhiteSpace(genre) || song.Year is not null)
        {
            sb.AppendLine(
                $"{Strings.Shell_MetaGenreYear}: {string.Join(" / ", new[] { genre, song.Year?.ToString() }.Where(s => !string.IsNullOrWhiteSpace(s)))}");
        }

        if (song.TrackNumber is not null)
            sb.AppendLine($"# {song.TrackNumber}");
        sb.AppendLine($"Duration: {duration}");
        if (song.Bitrate is not null)
            sb.AppendLine($"Bitrate: {song.Bitrate} kbps");
        if (song.SampleRate is not null)
            sb.AppendLine($"Sample rate: {song.SampleRate} Hz");
        if (!string.IsNullOrWhiteSpace(song.Composer))
            sb.AppendLine($"Composer: {song.Composer}");
        sb.AppendLine();
        sb.Append(song.FilePath);
        return sb.ToString();
    }
}
