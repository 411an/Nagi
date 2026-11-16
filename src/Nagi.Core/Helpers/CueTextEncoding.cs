using System.Text;

namespace Nagi.Core.Helpers;

/// <summary>Decodes CUE text while handling BOMs and common legacy encodings.</summary>
internal static class CueTextEncoding
{
    static CueTextEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (HasBom(bytes))
        {
            using var bomStream = new MemoryStream(bytes, writable: false);
            using var bomReader = new StreamReader(
                bomStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return bomReader.ReadToEnd();
        }

        var detector = new Ude.CharsetDetector();
        detector.Feed(bytes, 0, bytes.Length);
        detector.DataEnd();
        var encoding = detector.Charset is { } charset
            ? Encoding.GetEncoding(charset)
            : ATL.Settings.DefaultTextEncoding;

        if (encoding.CodePage is 10007 or 1251)
        {
            var guessedText = encoding.GetString(bytes);
            var candidate1251 = Encoding.GetEncoding(1251).GetString(bytes);
            var candidateMac = Encoding.GetEncoding(10007).GetString(bytes);
            var guessedCount = CountCyrillicLetters(guessedText);
            var candidate1251Count = CountCyrillicLetters(candidate1251);
            var candidateMacCount = CountCyrillicLetters(candidateMac);
            if (candidate1251Count > candidateMacCount && candidate1251Count > guessedCount)
                return candidate1251;
            if (candidateMacCount > candidate1251Count && candidateMacCount > guessedCount)
                return candidateMac;
            return guessedText;
        }

        return encoding.GetString(bytes);
    }

    private static bool HasBom(byte[] bytes) =>
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ||
        (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) ||
                               (bytes[0] == 0xFE && bytes[1] == 0xFF))) ||
        (bytes.Length >= 4 && ((bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00) ||
                               (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)));

    private static int CountCyrillicLetters(string value) =>
        value.Count(c => char.IsLetter(c) && c is >= '\u0400' and <= '\u04FF');
}
