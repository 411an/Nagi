using System.Text;
using FluentAssertions;
using Nagi.Core.Helpers;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class CueTextEncodingTests
{
    private const string CueText = "PERFORMER \"Группа\"\nTITLE \"Альбом\"\nFILE \"album.wav\" WAVE\nTRACK 01 AUDIO\nTITLE \"Первый трек\"\nPERFORMER \"Первый артист\"\nTRACK 02 AUDIO\nTITLE \"Второй трек\"\nPERFORMER \"Второй артист\"\n";

    static CueTextEncodingTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Decode_Windows1251ShortCue_ReturnsExactRussianText()
    {
        CueTextEncoding.Decode(Encoding.GetEncoding(1251).GetBytes(CueText))
            .Should().Be(CueText);
    }

    [Fact]
    public void Decode_MacCyrillicShortCue_PreservesMacDecoding()
    {
        var bytes = Encoding.GetEncoding(10007).GetBytes(CueText);
        CueTextEncoding.Decode(bytes).Should().Be(Encoding.GetEncoding(10007).GetString(bytes));
    }

    [Fact]
    public void Decode_Utf8WithoutBom_ReturnsExactText()
    {
        CueTextEncoding.Decode(new UTF8Encoding(false).GetBytes(CueText)).Should().Be(CueText);
    }

    [Fact]
    public void Decode_Utf8WithBom_ReturnsExactText()
    {
        CueTextEncoding.Decode(new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(CueText)).ToArray())
            .Should().Be(CueText);
    }

    [Fact]
    public void Decode_Utf16WithBom_ReturnsExactText()
    {
        CueTextEncoding.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(CueText)).ToArray())
            .Should().Be(CueText);
    }

    [Fact]
    public void Decode_Utf32WithBom_ReturnsExactText()
    {
        var utf32 = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        CueTextEncoding.Decode(utf32.GetPreamble().Concat(utf32.GetBytes(CueText)).ToArray())
            .Should().Be(CueText);
    }

    [Fact]
    public void Decode_Ascii_ReturnsExactText()
    {
        const string ascii = "PERFORMER \"Group\"\nTITLE \"Album\"\nFILE \"album.wav\" WAVE\n";
        CueTextEncoding.Decode(Encoding.ASCII.GetBytes(ascii)).Should().Be(ascii);
    }
}
