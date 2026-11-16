using System.Text;

namespace Nagi.Core.Helpers;

/// <summary>
///     Recovers Russian (and other CP1251) ID3 text that was stored as ISO-8859-1.
///     ID3 has no code-page flag for Windows-1251, so this is a conservative heuristic.
///     A Cyrillic file name is an extra vote to decode the tag as 1251.
/// </summary>
public static class TagTextEncoding
{
    private const int MinCyrillicLetters = 2;
    private const int MinFilenameCyrillicLetters = 2;
    private const int MinFilenameOverlap = 2;
    private const double MinCyrillicLetterRatio = 0.5;

    private static readonly Encoding Latin1 = Encoding.Latin1;
    private static readonly Encoding Windows1251;

    static TagTextEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1251 = Encoding.GetEncoding(1251);
    }

    public static string? RepairMislabelledWindows1251(string? value, string? filePath = null)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var highCount = 0;
        foreach (var c in value)
        {
            if (c > 255)
                return value;
            if (IsCyrillic(c))
                return value;
            if (c >= 0x80)
                highCount++;
        }

        if (highCount == 0)
            return value;

        var repaired = Windows1251.GetString(Latin1.GetBytes(value));
        if (LooksLikeCyrillicText(repaired) || FilenameSupportsRepair(value, repaired, filePath))
            return repaired;

        return value;
    }

    private static bool FilenameSupportsRepair(string original, string repaired, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (CountCyrillicLetters(fileName) < MinFilenameCyrillicLetters)
            return false;

        var repairedOverlap = CountSharedCyrillicLetters(repaired, fileName);
        var originalOverlap = CountSharedCyrillicLetters(original, fileName);
        return repairedOverlap >= MinFilenameOverlap && repairedOverlap > originalOverlap;
    }

    private static bool LooksLikeCyrillicText(string value)
    {
        var letters = 0;
        var cyrillic = 0;
        foreach (var c in value)
        {
            if (!char.IsLetter(c))
                continue;
            letters++;
            if (IsCyrillic(c))
                cyrillic++;
        }

        return cyrillic >= MinCyrillicLetters
               && letters > 0
               && (double)cyrillic / letters >= MinCyrillicLetterRatio;
    }

    private static int CountCyrillicLetters(string value)
    {
        var count = 0;
        foreach (var c in value)
        {
            if (IsCyrillic(c))
                count++;
        }

        return count;
    }

    private static int CountSharedCyrillicLetters(string value, string fileName)
    {
        var fileLetters = new HashSet<char>();
        foreach (var c in fileName)
        {
            if (IsCyrillic(c))
                fileLetters.Add(char.ToUpperInvariant(c));
        }

        if (fileLetters.Count == 0)
            return 0;

        var shared = 0;
        var seen = new HashSet<char>();
        foreach (var c in value)
        {
            if (!IsCyrillic(c))
                continue;
            var key = char.ToUpperInvariant(c);
            if (fileLetters.Contains(key) && seen.Add(key))
                shared++;
        }

        return shared;
    }

    private static bool IsCyrillic(char c) => c is >= '\u0400' and <= '\u04FF';
}
