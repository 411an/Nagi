namespace Nagi.Core.Models;

public enum SongTextSearchScope
{
    Library,
    Playlist,
    AlbumArtist
}

/// <summary>
///     A song matched by title, artist, album title, or file path. Used by the shell tree search.
/// </summary>
public sealed class SongTextHit
{
    public Guid SongId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public string ArtistName { get; init; } = string.Empty;

    public Guid RootFolderId { get; init; }

    public string DirectoryPath { get; init; } = string.Empty;

    public Guid? PlaylistId { get; init; }

    public string? PlaylistName { get; init; }

    public Guid? ArtistId { get; init; }
}
