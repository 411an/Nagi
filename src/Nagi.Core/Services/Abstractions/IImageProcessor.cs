namespace Nagi.Core.Services.Abstractions;

/// <summary>
///     Stores cover and artist images on disk. Implementations may resize or extract colors;
///     the portable fork keeps the original bytes and does not derive theme swatches.
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    ///     Saves image bytes to local storage, deduplicating identical content by hash.
    /// </summary>
    /// <returns>
    ///     The local file path and optional light/dark theme swatches. Swatches are unused
    ///     in the portable fork and are always null.
    /// </returns>
    Task<(string? uri, string? lightSwatchId, string? darkSwatchId)> SaveCoverArtAndExtractColorsAsync(
        byte[] pictureData);

    /// <summary>
    ///     Optional resize/convert hook for downloaded images. The portable store returns
    ///     the original bytes unchanged.
    /// </summary>
    Task<byte[]> ProcessImageBytesAsync(byte[] imageData, int maxDimension = 600);
}
