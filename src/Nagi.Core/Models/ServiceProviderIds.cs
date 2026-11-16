namespace Nagi.Core.Models;

/// <summary>
///     Constants for service provider identifiers used throughout the application.
///     Centralizes provider IDs to prevent typos and enable compile-time checking.
/// </summary>
public static class ServiceProviderIds
{
    #region Lyrics Providers

    /// <summary>LRCLIB</summary>
    public const string LrcLib = "lrclib";

    /// <summary>NetEase</summary>
    public const string NetEase = "netease";

    #endregion

    #region Metadata Providers

    /// <summary>MusicBrainz - Open music encyclopedia, provides artist/release IDs.</summary>
    public const string MusicBrainz = "musicbrainz";

    #endregion

    #region Internal Providers

    /// <summary>
    ///     Generic bucket for cover-art / artist-image downloads from third-party CDNs.
    ///     Used when the originating provider is not knowable at download time.
    /// </summary>
    public const string ImageDownload = "image-download";

    #endregion
}
