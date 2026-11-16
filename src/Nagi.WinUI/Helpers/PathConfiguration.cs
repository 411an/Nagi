using System.IO;
using Nagi.Core.Helpers;

namespace Nagi.WinUI.Helpers;

/// <summary>
///     Provides a centralized source of truth for all application data paths.
///     The data root is resolved once by <see cref="AppDataLocator" /> before this type is constructed.
/// </summary>
public class PathConfiguration : IPathConfiguration
{
    public PathConfiguration()
    {
        AppDataRoot = AppDataLocator.IsResolved
            ? AppDataLocator.Root
            : AppDataLocator.Resolve(AppContext.BaseDirectory, Environment.GetCommandLineArgs()).Root;

        SettingsFilePath = Path.Combine(AppDataRoot, "settings.json");
        PlaybackStateFilePath = Path.Combine(AppDataRoot, "playback_state.json");
        AlbumArtCachePath = Path.Combine(AppDataRoot, "AlbumArt");
        ArtistImageCachePath = Path.Combine(AppDataRoot, "ArtistImages");
        PlaylistImageCachePath = Path.Combine(AppDataRoot, "PlaylistImages");
        LrcCachePath = Path.Combine(AppDataRoot, "LrcCache");
        RomanizationPacksPath = Path.Combine(AppDataRoot, "RomanizationPacks");
        DatabasePath = Path.Combine(AppDataRoot, "nagi.db");
        LogsDirectory = Path.Combine(AppDataRoot, "Logs");
        ImageCachePath = Path.Combine(AppDataRoot, "Cache");

        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(AlbumArtCachePath);
        Directory.CreateDirectory(ArtistImageCachePath);
        Directory.CreateDirectory(PlaylistImageCachePath);
        Directory.CreateDirectory(LrcCachePath);
        Directory.CreateDirectory(RomanizationPacksPath);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ImageCachePath);
    }

    /// <inheritdoc />
    public string AppDataRoot { get; }

    /// <inheritdoc />
    public string SettingsFilePath { get; }

    /// <inheritdoc />
    public string PlaybackStateFilePath { get; }

    /// <inheritdoc />
    public string AlbumArtCachePath { get; }

    /// <inheritdoc />
    public string ArtistImageCachePath { get; }

    /// <inheritdoc />
    public string PlaylistImageCachePath { get; }

    /// <inheritdoc />
    public string LrcCachePath { get; }

    /// <inheritdoc />
    public string RomanizationPacksPath { get; }

    /// <inheritdoc />
    public string DatabasePath { get; }

    /// <inheritdoc />
    public string LogsDirectory { get; }

    /// <inheritdoc />
    public string ImageCachePath { get; }
}
