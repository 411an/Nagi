using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nagi.WinUI.Models;

public enum LibraryTreeNodeKind
{
    Folder,
    Playlist,
    Artist,
    Album,
    Song
}

/// <summary>
///     A tree node. For folders, <see cref="RootFolderId" /> is the library root and
///     <see cref="Path" /> is the directory; nested <see cref="FolderEntityId" /> is not a song FolderId.
/// </summary>
public partial class LibraryTreeNode : ObservableObject
{
    public LibraryTreeNodeKind Kind { get; init; }

    public Guid FolderEntityId { get; init; }

    public Guid RootFolderId { get; init; }

    public string Path { get; init; } = string.Empty;

    public Guid? PlaylistId { get; init; }

    public Guid? ArtistId { get; init; }

    public Guid? AlbumId { get; init; }

    public Guid? SongId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public bool IsLibraryRoot { get; init; }

    [ObservableProperty] public partial bool HasUnrealizedChildren { get; set; }

    [ObservableProperty] public partial bool IsChildrenLoaded { get; set; }

    public ObservableCollection<LibraryTreeNode> Children { get; } = new();
}
