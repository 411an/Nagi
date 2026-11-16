using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Nagi.Core.Helpers;
using Nagi.Core.Services.Abstractions;

namespace Nagi.Core.Services.Implementations;

/// <summary>
///     Stores album and artist images as the original bytes, keyed by content hash.
///     Does not decode, resize, or extract theme colors.
/// </summary>
public class CoverArtFileStore : IImageProcessor
{
    private readonly string _albumArtStoragePath;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<CoverArtFileStore> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inFlightSaves = new();

    public CoverArtFileStore(IPathConfiguration pathConfig, IFileSystemService fileSystem,
        ILogger<CoverArtFileStore> logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger;
        _albumArtStoragePath = pathConfig.AlbumArtCachePath;

        try
        {
            _fileSystem.CreateDirectory(_albumArtStoragePath);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to create Album Art directory at '{AlbumArtPath}'.", _albumArtStoragePath);
        }
    }

    public async Task<(string? uri, string? lightSwatchId, string? darkSwatchId)> SaveCoverArtAndExtractColorsAsync(
        byte[] pictureData)
    {
        if (pictureData.Length == 0)
            return (null, null, null);

        try
        {
            var contentHash = GenerateContentHash(pictureData);
            var existingFile = FindCachedFileByHash(contentHash);
            if (existingFile != null)
                return (existingFile, null, null);

            var path = await SaveNewImageAsync(contentHash, pictureData).ConfigureAwait(false);
            return (path, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save cover art.");
            return (null, null, null);
        }
    }

    public Task<byte[]> ProcessImageBytesAsync(byte[] imageData, int maxDimension = 600)
    {
        _ = maxDimension;
        return Task.FromResult(imageData);
    }

    private string? FindCachedFileByHash(string contentHash)
    {
        try
        {
            var matches = _fileSystem.GetFiles(_albumArtStoragePath, $"{contentHash}.*");
            return matches.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error searching for cached file with hash {Hash}", contentHash);
            return null;
        }
    }

    private async Task<string> SaveNewImageAsync(string contentHash, byte[] pictureData)
    {
        var lazyTask = _inFlightSaves.GetOrAdd(contentHash,
            _ => new Lazy<Task<string>>(() => SaveNewImageCoreAsync(contentHash, pictureData)));

        try
        {
            return await lazyTask.Value.ConfigureAwait(false);
        }
        finally
        {
            _inFlightSaves.TryRemove(contentHash, out _);
        }
    }

    private async Task<string> SaveNewImageCoreAsync(string contentHash, byte[] pictureData)
    {
        var existingFile = FindCachedFileByHash(contentHash);
        if (existingFile != null)
            return existingFile;

        var extension = DetectImageExtension(pictureData);
        var filename = $"{contentHash}.fetched{extension}";
        var fullPath = _fileSystem.Combine(_albumArtStoragePath, filename);
        var tempPath = fullPath + ".tmp";

        await _fileSystem.WriteAllBytesAsync(tempPath, pictureData).ConfigureAwait(false);

        try
        {
            _fileSystem.MoveFile(tempPath, fullPath, overwrite: false);
        }
        catch (IOException)
        {
            try { _fileSystem.DeleteFile(tempPath); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to clean up temp file {TempPath}", tempPath); }

            existingFile = FindCachedFileByHash(contentHash);
            if (existingFile != null)
                return existingFile;
        }

        return fullPath;
    }

    internal static string DetectImageExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ".jpg";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
            return ".png";
        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
            return ".gif";
        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return ".webp";
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
            return ".bmp";

        return ".jpg";
    }

    private static string GenerateContentHash(byte[] pictureData)
    {
        var hashBytes = SHA256.HashData(pictureData);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
