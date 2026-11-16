using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nagi.Core.Helpers;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Implementations;
using NSubstitute;
using Xunit;

namespace Nagi.Core.Tests;

public class LyricsTextServiceTests
{
    private const string LrcPath = "C:\\music\\song.lrc";
    private readonly IFileSystemService _fileSystem = Substitute.For<IFileSystemService>();
    private readonly IOnlineLyricsService _lrcLib = Substitute.For<IOnlineLyricsService>();
    private readonly INetEaseLyricsService _netEase = Substitute.For<INetEaseLyricsService>();
    private readonly ILibraryReader _libraryReader = Substitute.For<ILibraryReader>();
    private readonly LyricsTextService _service;

    public LyricsTextServiceTests()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetFetchOnlineLyricsEnabledAsync().Returns(true);
        var lrcService = new LrcService(_fileSystem, _lrcLib, _netEase, settings,
            Substitute.For<IPathConfiguration>(), Substitute.For<ILibraryWriter>(),
            Substitute.For<ILogger<LrcService>>());
        _service = new LyricsTextService(lrcService, _libraryReader);
    }

    private static Song CreateSong(string? lrcPath = null, string? lyrics = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Song",
        FilePath = "C:\\music\\song.mp3",
        LrcFilePath = lrcPath,
        Lyrics = lyrics
    };

    private void GivenLrcFile(string content)
    {
        _fileSystem.FileExists(LrcPath).Returns(true);
        _fileSystem.ReadAllTextAsync(LrcPath).Returns(content);
    }

    private void GivenTagText(Song song, string? text) =>
        _libraryReader.GetSongWithFullDataAsync(song.Id).Returns(new Song { Id = song.Id, Lyrics = text });

    [Fact]
    public async Task LrcFile_GivesLinesInTimeOrderWithoutTimestamps()
    {
        GivenLrcFile("[ar:Someone]\n[00:09.00]Third\n[00:01.00]First\n[00:05.00]Second");
        var song = CreateSong(LrcPath);
        GivenTagText(song, "Tag text");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().Be("First\nSecond\nThird");
    }

    [Fact]
    public async Task LrcFile_EmptyTimedLinesBecomeOneBlankLine()
    {
        GivenLrcFile("[00:01.00]One\n[00:02.00]\n[00:03.00]\n[00:04.00]Two\n[00:05.00]");

        var text = await _service.GetLyricsTextAsync(CreateSong(LrcPath));

        text.Should().Be("One\n\nTwo");
    }

    [Fact]
    public async Task NoLrcFile_GivesTagTextFromLibrary()
    {
        var song = CreateSong();
        GivenTagText(song, "Line one\r\nLine two");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().Be("Line one\nLine two");
    }

    [Fact]
    public async Task MissingLrcFile_FallsBackToTagText()
    {
        _fileSystem.FileExists(LrcPath).Returns(false);
        var song = CreateSong(LrcPath);
        GivenTagText(song, "From the tag");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().Be("From the tag");
    }

    [Fact]
    public async Task LrcFileAddedAfterScan_IsFoundBesideTheTrack()
    {
        const string sidecar = "C:\\music\\song.lrc";
        _fileSystem.GetDirectoryName("C:\\music\\song.mp3").Returns("C:\\music");
        _fileSystem.GetFileNameWithoutExtension(Arg.Any<string>())
            .Returns(call => Path.GetFileNameWithoutExtension(call.Arg<string>()));
        _fileSystem.GetFiles("C:\\music", "*.lrc").Returns(["C:\\music\\other.lrc", sidecar]);
        _fileSystem.FileExists(sidecar).Returns(true);
        _fileSystem.ReadAllTextAsync(sidecar).Returns("[00:01.00]From the sidecar");
        var song = CreateSong();
        GivenTagText(song, "Tag text");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().Be("From the sidecar");
    }

    [Fact]
    public async Task CueTrack_IgnoresFileNamedAfterTheAlbumFile()
    {
        _fileSystem.GetDirectoryName(Arg.Any<string>()).Returns("C:\\music");
        _fileSystem.GetFileNameWithoutExtension(Arg.Any<string>()).Returns("song");
        _fileSystem.GetFiles("C:\\music", "*.lrc").Returns(["C:\\music\\song.lrc"]);
        _fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        _fileSystem.ReadAllTextAsync(Arg.Any<string>()).Returns("[00:01.00]Whole album");
        var song = CreateSong();
        song.CueTrackNumber = 3;
        GivenTagText(song, "Tag text");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().Be("Tag text");
    }

    [Fact]
    public async Task TagTextIsTidied()
    {
        var song = CreateSong();
        GivenTagText(song, "eng||\r\n\r\n  Verse one  \r\nline\r\n\r\n\r\n\r\nVerse two\r\n\r\n");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().Be("Verse one\nline\n\nVerse two");
    }

    [Fact]
    public async Task TimedTagText_GivesLinesWithoutTimestamps()
    {
        var song = CreateSong();
        GivenTagText(song, "[00:02.00]Second\n[00:01.00]First");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().Be("First\nSecond");
    }

    [Fact]
    public async Task SongOutsideLibrary_UsesItsOwnTagText()
    {
        var song = CreateSong(lyrics: "Carried by the file");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().Be("Carried by the file");
        await _libraryReader.DidNotReceive().GetSongWithFullDataAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task NoLyrics_GivesNullAndStaysOffline()
    {
        var song = CreateSong();
        GivenTagText(song, "   ");

        var text = await _service.GetLyricsTextAsync(song);

        text.Should().BeNull();
        await _lrcLib.DidNotReceiveWithAnyArgs().GetLyricsAsync(default!, default, default, default, default);
        await _netEase.DidNotReceiveWithAnyArgs().SearchLyricsAsync(default!, default, default);
    }
}
