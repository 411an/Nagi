using System.Text.RegularExpressions;
using Nagi.Core.Models;

namespace Nagi.Core.Helpers;

/// <summary>
///     Resolves artist / album-artist / title when tags are missing or useless.
/// </summary>
/// <remarks>
///     File-name rules:
///     <list type="bullet">
///         <item>Leading track index (<c>15 - </c>, <c>32 </c>, <c>03.</c>) is stripped; a leftover leading dash is stripped next.</item>
///         <item>Only a <b>spaced</b> single dash (<c> - </c>, <c> – </c>, <c> — </c>) splits artist from title.</item>
///         <item>Double dash (<c>--</c> / <c> -- </c>) and glued hyphens (<c>Баю-баю</c>) stay inside the text.</item>
///         <item>If there is no spaced single-dash split, the whole remainder is the title and the artist comes from the top folder under the library root.</item>
///     </list>
/// </remarks>
public static class ArtistCreditResolver
{
    /// <summary>Spaced single-dash only — never <c>--</c>.</summary>
    private static readonly string[] ArtistTitleSeparators = [" - ", " – ", " — "];

    // "15 - ", "34-", "03. ", "02_", or "32 " (digits + whitespace, no dash).
    private static readonly Regex LeadingTrackNumber = new(
        @"^\d{1,3}(?:\s*[-–—._]\s*|\s+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LeadingDashDebris = new(
        @"^[-–—._]+\s*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsMissingCredit(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;

        var trimmed = name.Trim();
        if (trimmed.Equals(Artist.UnknownArtistName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.Length <= 3 && trimmed.All(char.IsDigit))
            return true;

        return trimmed.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("<Unknown>", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Strips a leading track index, then any leftover leading dash/dot debris.
    /// </summary>
    public static string StripLeadingTrackNumber(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return fileName;

        var value = fileName.Trim();
        value = LeadingTrackNumber.Replace(value, string.Empty);
        value = LeadingDashDebris.Replace(value, string.Empty);
        return value.Trim();
    }

    /// <summary>
    ///     Cleans a tag that may start with a track index (<c>32 Наша Родина</c>, <c>15 - Something</c>).
    /// </summary>
    public static string? CleanCreditTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var cleaned = ArtistNameHelper.NormalizeStringCore(StripLeadingTrackNumber(tag));
        return IsMissingCredit(cleaned) ? null : cleaned;
    }

    public static string ResolveCredit(
        IEnumerable<string?> candidates,
        string filePath,
        string? libraryRootPath)
    {
        return Resolve(candidates, filePath, libraryRootPath).Artist;
    }

    /// <summary>
    ///     Full resolve: artist credit + preferred title from the file name when useful.
    /// </summary>
    public static CreditResolveResult Resolve(
        IEnumerable<string?> candidates,
        string filePath,
        string? libraryRootPath)
    {
        var strippedFileName = StripLeadingTrackNumber(Path.GetFileNameWithoutExtension(filePath) ?? string.Empty);
        var hasSpacedArtistTitleSplit = TrySplitArtistTitle(strippedFileName, out var fileArtist, out var fileTitle);

        foreach (var candidate in candidates)
        {
            var cleaned = CleanCreditTag(candidate);
            if (cleaned is null) continue;

            // Tag that is only the song-name fragment when the file has no "Artist - Title" split
            // (e.g. tag "32 Наша Родина" → "Наша Родина" on file "32 Наша Родина -- СССР.mp3").
            if (!hasSpacedArtistTitleSplit
                && !string.IsNullOrEmpty(strippedFileName)
                && strippedFileName.StartsWith(cleaned, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new CreditResolveResult(
                cleaned,
                fileTitle ?? strippedFileName,
                hasSpacedArtistTitleSplit,
                ArtistFromTags: true);
        }

        if (hasSpacedArtistTitleSplit && !IsMissingCredit(fileArtist))
            return new CreditResolveResult(fileArtist!, fileTitle, true, ArtistFromTags: false);

        if (TryGetLibraryChildFolderName(filePath, libraryRootPath, out var fromFolder)
            && !IsMissingCredit(fromFolder))
        {
            return new CreditResolveResult(fromFolder!, strippedFileName, false, ArtistFromTags: false);
        }

        return new CreditResolveResult(Artist.UnknownArtistName, strippedFileName, false, ArtistFromTags: false);
    }

    public static bool TryParseArtistFromFileName(string filePath, out string? artist)
    {
        return TryParseArtistAndTitleFromFileName(filePath, out artist, out _);
    }

    public static bool TryParseArtistAndTitleFromFileName(string filePath, out string? artist, out string? title)
    {
        artist = null;
        title = null;
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        var fileName = StripLeadingTrackNumber(Path.GetFileNameWithoutExtension(filePath));
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        if (!TrySplitArtistTitle(fileName, out artist, out title))
            return false;

        return !IsMissingCredit(artist);
    }

    /// <summary>
    ///     Splits only on a spaced single dash. Double dash and glued hyphens are left alone.
    /// </summary>
    public static bool TrySplitArtistTitle(string fileNameWithoutExtension, out string? artist, out string? title)
    {
        artist = null;
        title = null;
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension)) return false;

        var bestIndex = -1;
        string? bestSeparator = null;

        foreach (var separator in ArtistTitleSeparators)
        {
            var index = fileNameWithoutExtension.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0) continue;

            // Prefer the leftmost spaced single-dash (after a possible "15 - " already stripped).
            if (bestIndex < 0 || index < bestIndex)
            {
                bestIndex = index;
                bestSeparator = separator;
            }
        }

        if (bestIndex < 0 || bestSeparator is null) return false;

        // Reject if this " - " is actually the start of " -- " (double dash with spaces).
        var after = bestIndex + bestSeparator.Length;
        if (after < fileNameWithoutExtension.Length)
        {
            var ch = fileNameWithoutExtension[after];
            if (ch is '-' or '–' or '—')
                return false;
        }

        var left = ArtistNameHelper.NormalizeStringCore(fileNameWithoutExtension[..bestIndex]);
        var right = ArtistNameHelper.NormalizeStringCore(fileNameWithoutExtension[after..]);
        if (IsMissingCredit(left) || string.IsNullOrEmpty(right)) return false;

        artist = left;
        title = right;
        return true;
    }

    public static bool TryGetLibraryChildFolderName(string filePath, string? libraryRootPath, out string? folderName)
    {
        folderName = null;
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(libraryRootPath))
            return false;

        var fullFile = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(libraryRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!fullFile.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !fullFile.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;

        var relative = fullFile[(root.Length + 1)..];
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2) return false;

        folderName = ArtistNameHelper.NormalizeStringCore(segments[0]);
        return !IsMissingCredit(folderName);
    }

    public readonly record struct CreditResolveResult(
        string Artist,
        string? PreferredTitleFromFile,
        bool FileNameHadArtistTitleSplit,
        bool ArtistFromTags);
}
