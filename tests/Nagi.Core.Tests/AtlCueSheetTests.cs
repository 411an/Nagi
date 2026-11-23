using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nagi.Core.Helpers;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using NSubstitute;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class AtlCueSheetTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "nagi-cue-" + Guid.NewGuid().ToString("N"));
    private readonly IFileSystemService _fileSystem = Substitute.For<IFileSystemService>();
    private readonly ILogger _logger = Substitute.For<ILogger>();

    public AtlCueSheetTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(_tempDirectory);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("windows1251")]
    public async Task ExpandAsync_ExpandsRussianSingleFileCue(string encodingName)
    {
        var audioPath = CreateWav("album.wav");
        var cuePath = Path.Combine(_tempDirectory, "album.cue");
        var encoding = encodingName == "windows1251"
            ? Encoding.GetEncoding(1251)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var cue = """
            PERFORMER "Группа"
            TITLE "Альбом"
            FILE "album.wav" WAVE
              TRACK 01 AUDIO
                TITLE "Первый трек"
                PERFORMER "Первый артист"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Второй трек"
                PERFORMER "Второй артист"
                INDEX 01 00:01:10
            """;
        File.WriteAllBytes(cuePath, encoding.GetBytes(cue));
        ConfigureCueFiles(cuePath);

        var metadata = CreateMetadata(audioPath);
        await CueSheetReader.ExpandAsync(metadata, _fileSystem, _logger);

        metadata.CueReadFailed.Should().BeFalse();
        metadata.CueTracks.Should().HaveCount(2);
        metadata.CueTracks.Select(track => track.FilePath).Should().AllBeEquivalentTo(audioPath);
        metadata.CueTracks.Select(track => track.CueTrackNumber).Should().Equal(1, 2);
        metadata.CueTracks[0].StartOffsetTicks.Should().Be(0);
        metadata.CueTracks[1].StartOffsetTicks.Should()
            .Be(TimeSpan.TicksPerSecond + 10 * TimeSpan.TicksPerSecond / 75);
        metadata.CueTracks.Sum(track => track.Duration.Ticks).Should().Be(TimeSpan.FromSeconds(5).Ticks);
        metadata.CueTracks[0].Album.Should().Be("Альбом");
        metadata.CueTracks[0].AlbumArtists.Should().ContainSingle().Which.Should().Be("Группа");
        metadata.CueTracks[0].Title.Should().Be("Первый трек");
        metadata.CueTracks[1].Artists.Should().ContainSingle().Which.Should().Be("Второй артист");
        metadata.CueTracks.Should().AllSatisfy(track =>
        {
            track.Lyrics.Should().BeNull();
            track.LrcFilePath.Should().BeNull();
            track.CoverArtUri.Should().Be("cover.jpg");
        });
    }

    [Fact]
    public async Task ExpandAsync_ConcurrentMetadataReadsRemainComplete()
    {
        var audioPath = CreateWav("concurrent.wav");
        var cuePath = WriteCue("concurrent.cue", """
            FILE "concurrent.wav" WAVE
              TRACK 01 AUDIO
                TITLE "First"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Second"
                INDEX 01 00:01:10
            """);
        ConfigureCueFiles(cuePath);

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            var metadata = CreateMetadata(audioPath);
            await CueSheetReader.ExpandAsync(metadata, _fileSystem, _logger);
            return metadata;
        })));

        results.Should().HaveCount(32);
        results.Should().AllSatisfy(metadata =>
        {
            metadata.CueReadFailed.Should().BeFalse();
            metadata.CueTracks.Should().HaveCount(2);
            metadata.CueTracks.Select(track => track.CueTrackNumber).Should().Equal(1, 2);
            metadata.CueTracks.Select(track => track.Title).Should().Equal("First", "Second");
            metadata.CueTracks.Select(track => track.StartOffsetTicks).Should().Equal(
                0, TimeSpan.TicksPerSecond + 10 * TimeSpan.TicksPerSecond / 75);
        });
    }

    [Fact]
    public async Task ExpandAsync_IgnoresIndex00BeforePlaybackIndex()
    {
        var audioPath = CreateWav("index00.wav");
        var cuePath = WriteCue("index00.cue", $"""
            FILE "index00.wav" WAVE
              TRACK 01 AUDIO
                TITLE "First"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Second"
                INDEX 00 00:00:00
                INDEX 01 00:01:10
            """);
        ConfigureCueFiles(cuePath);
        var metadata = CreateMetadata(audioPath);

        await CueSheetReader.ExpandAsync(metadata, _fileSystem, _logger);

        metadata.CueReadFailed.Should().BeFalse();
        metadata.CueTracks.Select(track => track.StartOffsetTicks).Should().Equal(
            0, TimeSpan.TicksPerSecond + 10 * TimeSpan.TicksPerSecond / 75);
    }

    [Fact]
    public async Task ExpandAsync_DifferentFileLeavesCueTracksEmpty()
    {
        var audioPath = CreateWav("actual.wav");
        var cuePath = WriteCue("other.cue", "FILE \"other.wav\" WAVE\n  TRACK 01 AUDIO\n    INDEX 01 00:00:00\n");
        ConfigureCueFiles(cuePath);
        var metadata = CreateMetadata(audioPath);

        await CueSheetReader.ExpandAsync(metadata, _fileSystem, _logger);

        metadata.CueTracks.Should().BeEmpty();
        metadata.CueReadFailed.Should().BeFalse();
    }

    [Theory]
    [InlineData("missing-index.cue", "FILE \"missing-index.wav\" WAVE\n  TRACK 01 AUDIO\n    TITLE \"Track\"\n")]
    [InlineData("backward-index.cue", "FILE \"backward.wav\" WAVE\n  TRACK 01 AUDIO\n    INDEX 01 00:02:00\n  TRACK 02 AUDIO\n    INDEX 01 00:01:00\n")]
    public async Task ExpandAsync_InvalidIndicesSetReadFailed(string cueName, string cue)
    {
        var audioName = cueName.StartsWith("backward", StringComparison.Ordinal) ? "backward.wav" : "missing-index.wav";
        var audioPath = CreateWav(audioName);
        var cuePath = WriteCue(cueName, cue);
        ConfigureCueFiles(cuePath);
        var metadata = CreateMetadata(audioPath);

        await CueSheetReader.ExpandAsync(metadata, _fileSystem, _logger);

        metadata.CueTracks.Should().BeEmpty();
        metadata.CueReadFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ExpandAsync_NoSidecarLeavesWholeFileMetadataUnchanged()
    {
        var audioPath = CreateWav("plain.wav");
        var metadata = CreateMetadata(audioPath);
        var originalTitle = metadata.Title;
        var originalDuration = metadata.Duration;
        var originalCoverArt = metadata.CoverArtUri;
        var originalLyrics = metadata.Lyrics;
        var originalLrcPath = metadata.LrcFilePath;
        _fileSystem.GetFiles(Arg.Any<string>(), "*.cue").Returns([]);

        await CueSheetReader.ExpandAsync(metadata, _fileSystem, _logger);

        metadata.CueTracks.Should().BeEmpty();
        metadata.CueReadFailed.Should().BeFalse();
        metadata.Title.Should().Be(originalTitle);
        metadata.Duration.Should().Be(originalDuration);
        metadata.CoverArtUri.Should().Be(originalCoverArt);
        metadata.Lyrics.Should().Be(originalLyrics);
        metadata.LrcFilePath.Should().Be(originalLrcPath);
    }

    [Fact]
    public async Task ExpandAsync_MultiFileCueSetsReadFailed()
    {
        var audioPath = CreateWav("multi.wav");
        CreateWav("second.wav");
        var cuePath = WriteCue("multi.cue", """
            FILE "multi.wav" WAVE
              TRACK 01 AUDIO
                INDEX 01 00:00:00
            FILE "second.wav" WAVE
              TRACK 02 AUDIO
                INDEX 01 00:01:00
            """);
        ConfigureCueFiles(cuePath);
        var metadata = CreateMetadata(audioPath);

        await CueSheetReader.ExpandAsync(metadata, _fileSystem, _logger);

        metadata.CueTracks.Should().BeEmpty();
        metadata.CueReadFailed.Should().BeTrue();
    }

    private SongFileMetadata CreateMetadata(string filePath) => new()
    {
        FilePath = filePath,
        Title = "Whole file",
        Album = "Original album",
        Artists = ["Original artist"],
        AlbumArtists = ["Original album artist"],
        Duration = TimeSpan.FromSeconds(5),
        CoverArtUri = "cover.jpg",
        Lyrics = "lyrics",
        LrcFilePath = "lyrics.lrc"
    };

    private void ConfigureCueFiles(string cuePath)
    {
        _fileSystem.GetFiles(Arg.Any<string>(), "*.cue").Returns([cuePath]);
        _fileSystem.ReadAllBytesAsync(cuePath).Returns(_ => Task.FromResult(File.ReadAllBytes(cuePath)));
        _fileSystem.GetLastWriteTimeUtc(cuePath).Returns(File.GetLastWriteTimeUtc(cuePath));
    }

    private string WriteCue(string name, string contents)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    private string CreateWav(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var dataSize = sampleRate * 5 * channels * (bitsPerSample / 8);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }
}
