using System.Text;
using FluentAssertions;
using Nagi.Core.Helpers;
using Xunit;

namespace Nagi.Core.Tests;

public class TagTextEncodingTests
{
    public TagTextEncodingTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Repair_Windows1251Mojibake_BecomesCyrillic()
    {
        var original = "Номинации, 29/10/11";
        var mojibake = Encoding.Latin1.GetString(Encoding.GetEncoding(1251).GetBytes(original));

        mojibake.Should().NotBe(original);
        TagTextEncoding.RepairMislabelledWindows1251(mojibake).Should().Be(original);
    }

    [Fact]
    public void Repair_AlreadyCyrillic_LeavesUnchanged()
    {
        const string value = "Номинации";
        TagTextEncoding.RepairMislabelledWindows1251(value).Should().Be(value);
    }

    [Fact]
    public void Repair_Ascii_LeavesUnchanged()
    {
        const string value = "Rock";
        TagTextEncoding.RepairMislabelledWindows1251(value).Should().Be(value);
    }

    [Fact]
    public void Repair_WesternLatin1_LeavesUnchanged()
    {
        TagTextEncoding.RepairMislabelledWindows1251("café").Should().Be("café");
        TagTextEncoding.RepairMislabelledWindows1251("Größe").Should().Be("Größe");
    }

    [Fact]
    public void Repair_WesternLatin1_WithRussianFileName_StillLeavesUnchanged()
    {
        TagTextEncoding.RepairMislabelledWindows1251("café", @"C:\music\Кафе.mp3").Should().Be("café");
    }

    [Fact]
    public void Repair_MixedMojibake_WithMatchingRussianFileName_Converts()
    {
        var cyrillic = "Ном";
        var mojibake = Encoding.Latin1.GetString(Encoding.GetEncoding(1251).GetBytes(cyrillic));
        var tagged = "Live " + mojibake;
        var filePath = @"C:\music\Номинации 29-10-11.mp3";

        TagTextEncoding.RepairMislabelledWindows1251(tagged).Should().Be(tagged);
        TagTextEncoding.RepairMislabelledWindows1251(tagged, filePath).Should().Be("Live " + cyrillic);
    }

    [Fact]
    public void Repair_NullOrEmpty_ReturnsSame()
    {
        TagTextEncoding.RepairMislabelledWindows1251(null).Should().BeNull();
        TagTextEncoding.RepairMislabelledWindows1251("").Should().Be("");
    }
}
