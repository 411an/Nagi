using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nagi.Core.Helpers;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Implementations;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Nagi.Core.Tests;

public class CoverArtFileStoreTests
{
    private const string AlbumArtPath = "C:\\cache\\art";
    private readonly IFileSystemService _fileSystem;
    private readonly CoverArtFileStore _store;

    public CoverArtFileStoreTests()
    {
        var pathConfig = Substitute.For<IPathConfiguration>();
        _fileSystem = Substitute.For<IFileSystemService>();
        var logger = Substitute.For<ILogger<CoverArtFileStore>>();

        pathConfig.AlbumArtCachePath.Returns(AlbumArtPath);
        _fileSystem.Combine(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => Path.Combine(callInfo.ArgAt<string[]>(0)));

        _store = new CoverArtFileStore(pathConfig, _fileSystem, logger);
    }

    [Fact]
    public void Constructor_WhenCalled_CreatesAlbumArtDirectory()
    {
        _fileSystem.Received(1).CreateDirectory(AlbumArtPath);
    }

    [Fact]
    public async Task SaveCoverArt_WhenFileDoesNotExist_SavesOriginalBytesWithoutSwatches()
    {
        var pictureData = CreatePngBytes();
        var contentHash = GenerateContentHash(pictureData);
        _fileSystem.GetFiles(AlbumArtPath, $"{contentHash}.*").Returns(Array.Empty<string>());

        var (uri, lightSwatch, darkSwatch) = await _store.SaveCoverArtAndExtractColorsAsync(pictureData);

        uri.Should().Be(Path.Combine(AlbumArtPath, $"{contentHash}.fetched.png"));
        lightSwatch.Should().BeNull();
        darkSwatch.Should().BeNull();
        await _fileSystem.Received(1).WriteAllBytesAsync(Arg.Any<string>(), pictureData);
        _fileSystem.Received(1).MoveFile(Arg.Any<string>(), Arg.Any<string>(), false);
    }

    [Fact]
    public async Task SaveCoverArt_WhenFileAlreadyExists_SkipsSave()
    {
        var pictureData = CreatePngBytes();
        var contentHash = GenerateContentHash(pictureData);
        var cachedPath = Path.Combine(AlbumArtPath, $"{contentHash}.fetched.png");
        _fileSystem.GetFiles(AlbumArtPath, $"{contentHash}.*").Returns(new[] { cachedPath });

        var (uri, lightSwatch, darkSwatch) = await _store.SaveCoverArtAndExtractColorsAsync(pictureData);

        uri.Should().Be(cachedPath);
        lightSwatch.Should().BeNull();
        darkSwatch.Should().BeNull();
        await _fileSystem.DidNotReceive().WriteAllBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>());
    }

    [Fact]
    public async Task SaveCoverArt_WithIdenticalData_UsesSameCacheFile()
    {
        var pictureData = CreatePngBytes();
        var contentHash = GenerateContentHash(pictureData);
        string? savedPath = null;

        _fileSystem.GetFiles(AlbumArtPath, $"{contentHash}.*")
            .Returns(_ => savedPath == null ? Array.Empty<string>() : new[] { savedPath });
        _fileSystem.When(x => x.MoveFile(Arg.Any<string>(), Arg.Any<string>(), false))
            .Do(callInfo => savedPath = callInfo.ArgAt<string>(1));

        var (uri1, _, _) = await _store.SaveCoverArtAndExtractColorsAsync(pictureData);
        var (uri2, _, _) = await _store.SaveCoverArtAndExtractColorsAsync(pictureData);

        uri1.Should().Be(savedPath);
        uri2.Should().Be(savedPath);
        await _fileSystem.Received(1).WriteAllBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>());
        _fileSystem.Received(1).MoveFile(Arg.Any<string>(), Arg.Any<string>(), false);
    }

    [Fact]
    public async Task SaveCoverArt_WhenFileWriteFails_ReturnsNullValues()
    {
        var pictureData = CreatePngBytes();
        var contentHash = GenerateContentHash(pictureData);
        _fileSystem.GetFiles(AlbumArtPath, $"{contentHash}.*").Returns(Array.Empty<string>());
        _fileSystem.WriteAllBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>()).ThrowsAsync(new IOException("Disk full"));

        var (uri, lightSwatch, darkSwatch) = await _store.SaveCoverArtAndExtractColorsAsync(pictureData);

        uri.Should().BeNull();
        lightSwatch.Should().BeNull();
        darkSwatch.Should().BeNull();
    }

    [Fact]
    public async Task SaveCoverArt_WithEmptyData_ReturnsNullValues()
    {
        var (uri, lightSwatch, darkSwatch) = await _store.SaveCoverArtAndExtractColorsAsync(Array.Empty<byte>());

        uri.Should().BeNull();
        lightSwatch.Should().BeNull();
        darkSwatch.Should().BeNull();
        await _fileSystem.DidNotReceive().WriteAllBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>());
    }

    [Fact]
    public async Task ProcessImageBytesAsync_ReturnsOriginalBytes()
    {
        var imageData = CreatePngBytes();

        var result = await _store.ProcessImageBytesAsync(imageData, maxDimension: 300);

        result.Should().BeSameAs(imageData);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ".gif")]
    [InlineData(new byte[] { 0x42, 0x4D, 0x00, 0x00 }, ".bmp")]
    public void DetectImageExtension_RecognizesCommonHeaders(byte[] data, string expected)
    {
        CoverArtFileStore.DetectImageExtension(data).Should().Be(expected);
    }

    private static byte[] CreatePngBytes()
    {
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    }

    private static string GenerateContentHash(byte[] pictureData)
    {
        var hashBytes = SHA256.HashData(pictureData);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
