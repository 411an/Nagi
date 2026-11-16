using FluentAssertions;
using Nagi.Core.Helpers;
using Nagi.Core.Models;
using Xunit;

namespace Nagi.Core.Tests;

public class ArtistCreditResolverTests
{
    [Fact]
    public void Resolve_PrefersArtistTag()
    {
        ArtistCreditResolver.ResolveCredit(
                ["Yello", "Album Artist"],
                @"D:\Music\Armin\track.mp3",
                @"D:\Music")
            .Should().Be("Yello");
    }

    [Fact]
    public void Resolve_SkipsUnknown_UsesAlbumArtist()
    {
        ArtistCreditResolver.ResolveCredit(
                [Artist.UnknownArtistName, "Armin van Buuren"],
                @"D:\Music\Other\track.mp3",
                @"D:\Music")
            .Should().Be("Armin van Buuren");
    }

    [Fact]
    public void Resolve_FromFileName_ArtistDashTitle()
    {
        ArtistCreditResolver.ResolveCredit(
                [null, ""],
                @"D:\Music\Singles\Yello - Vicious Games.mp3",
                @"D:\Music")
            .Should().Be("Yello");
    }

    [Fact]
    public void Resolve_FromLibraryChildFolder_NotMusicRoot()
    {
        ArtistCreditResolver.ResolveCredit(
                [null],
                @"D:\Music\Armin van Buuren\A State Of Trance\01.mp3",
                @"D:\Music")
            .Should().Be("Armin van Buuren");
    }

    [Fact]
    public void Resolve_FileDirectlyInLibraryRoot_FallsBackToUnknown()
    {
        ArtistCreditResolver.ResolveCredit(
                [null],
                @"D:\Music\orphan.mp3",
                @"D:\Music")
            .Should().Be(Artist.UnknownArtistName);
    }

    [Theory]
    [InlineData("Artist - Title", "Artist", "Title")]
    [InlineData("Artist – Title", "Artist", "Title")]
    [InlineData("Artist — Title", "Artist", "Title")]
    [InlineData("15 - Modern Gustin Trio - Ticket To Ride", "Modern Gustin Trio", "Ticket To Ride")]
    [InlineData("34 - Аккорд – У моря, у синего моря", "Аккорд", "У моря, у синего моря")]
    [InlineData("03 - Аккорд - Баю-баю", "Аккорд", "Баю-баю")]
    public void ParseFileName_SpacedSingleDash_Splits(string name, string expectedArtist, string expectedTitle)
    {
        ArtistCreditResolver.TryParseArtistAndTitleFromFileName($@"C:\x\{name}.mp3", out var artist, out var title)
            .Should().BeTrue();
        artist.Should().Be(expectedArtist);
        title.Should().Be(expectedTitle);
    }

    [Fact]
    public void ParseFileName_DoubleDash_DoesNotSplit_UsesFolderAsArtist()
    {
        var path =
            @"E:\Music\Сергей Калугин и Оргия праведников - Коллекция (1989-2018)\Бутлеги\2013 - бутлег\32 Наша Родина -- СССР.mp3";

        ArtistCreditResolver.TryParseArtistAndTitleFromFileName(path, out _, out _)
            .Should().BeFalse();

        var result = ArtistCreditResolver.Resolve(
            ["32 Наша Родина", null],
            path,
            @"E:\Music");

        result.Artist.Should().Be("Сергей Калугин и Оргия праведников - Коллекция (1989-2018)");
        result.PreferredTitleFromFile.Should().Be("Наша Родина -- СССР");
        result.FileNameHadArtistTitleSplit.Should().BeFalse();
        result.ArtistFromTags.Should().BeFalse();
    }

    [Theory]
    [InlineData("Artist - Title", true, "Artist")]
    [InlineData("Artist -- Title", false, null)]
    public void ParseFileName_DoubleDash_IsNotSeparator(string name, bool expectOk, string? expectedArtist)
    {
        var ok = ArtistCreditResolver.TryParseArtistFromFileName($@"C:\x\{name}.mp3", out var artist);
        ok.Should().Be(expectOk);
        if (expectOk)
            artist.Should().Be(expectedArtist);
    }

    [Fact]
    public void StripLeadingTrackNumber_DigitsWithSpaceOnly()
    {
        ArtistCreditResolver.StripLeadingTrackNumber("32 Наша Родина -- СССР")
            .Should().Be("Наша Родина -- СССР");
    }

    [Fact]
    public void StripLeadingTrackNumber_LeftoverDashDebris()
    {
        ArtistCreditResolver.StripLeadingTrackNumber("15 - - Modern")
            .Should().Be("Modern");
    }

    [Fact]
    public void Resolve_SkipsNumericArtistTag_UsesFileName()
    {
        ArtistCreditResolver.ResolveCredit(
                ["15", null],
                @"D:\Music\Jazz\15 - Modern Gustin Trio - Ticket To Ride.mp3",
                @"D:\Music")
            .Should().Be("Modern Gustin Trio");
    }

    [Fact]
    public void IsMissingCredit_PureDigits_IsTrue()
    {
        ArtistCreditResolver.IsMissingCredit("34").Should().BeTrue();
        ArtistCreditResolver.IsMissingCredit("15").Should().BeTrue();
    }
}
