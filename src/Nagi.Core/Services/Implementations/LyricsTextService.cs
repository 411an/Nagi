using System.Text.RegularExpressions;
using Nagi.Core.Models;
using Nagi.Core.Models.Lyrics;
using Nagi.Core.Services.Abstractions;

namespace Nagi.Core.Services.Implementations;

/// <inheritdoc cref="ILyricsTextService" />
public sealed partial class LyricsTextService : ILyricsTextService
{
    private readonly ILibraryReader _libraryReader;
    private readonly ILrcService _lrcService;

    public LyricsTextService(ILrcService lrcService, ILibraryReader libraryReader)
    {
        _lrcService = lrcService;
        _libraryReader = libraryReader;
    }

    /// <inheritdoc />
    public async Task<string?> GetLyricsTextAsync(Song song, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);

        if (_lrcService.FindLocalLyricsPath(song) is { } lyricsPath)
        {
            var fromFile = ToPlainText(await _lrcService.GetLyricsAsync(lyricsPath).ConfigureAwait(false));
            if (fromFile is not null) return fromFile;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Library queries leave the tag text out of the song, so it is read on demand. Files played
        // from outside the library carry it already.
        var tagText = song.Lyrics;
        if (string.IsNullOrWhiteSpace(tagText))
            tagText = (await _libraryReader.GetSongWithFullDataAsync(song.Id).ConfigureAwait(false))?.Lyrics;

        return string.IsNullOrWhiteSpace(tagText) ? null : ToPlainText(_lrcService.ParseLyrics(tagText));
    }

    /// <summary>
    ///     Joins timed lines in time order, or takes the untimed text as it is. Lines are trimmed,
    ///     runs of blank lines become one, and blank lines at either end are dropped.
    /// </summary>
    public static string? ToPlainText(ParsedLrc? lyrics)
    {
        if (lyrics is null) return null;

        IEnumerable<string> lines = lyrics.IsEmpty
            ? LanguagePrefix().Replace(lyrics.RawUnsyncedLyrics ?? string.Empty, string.Empty)
                .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            : lyrics.Lines.Select(line => line.Text);

        var result = new List<string>();
        var blankPending = false;
        foreach (var line in lines.Select(line => line.Trim()))
        {
            if (line.Length == 0)
            {
                blankPending = result.Count > 0;
                continue;
            }

            if (blankPending) result.Add(string.Empty);
            blankPending = false;
            result.Add(line);
        }

        return result.Count == 0 ? null : string.Join("\n", result);
    }

    // Some taggers store the language in front of the text, as in "eng||".
    [GeneratedRegex(@"^[a-z]{2,3}\|\|", RegexOptions.IgnoreCase)]
    private static partial Regex LanguagePrefix();
}
