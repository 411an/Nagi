using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Data;
using Nagi.WinUI.Helpers;
using Nagi.WinUI.Models;
using Nagi.WinUI.Services.Abstractions;

namespace Nagi.WinUI.ViewModels;

public enum LibraryTreeMode
{
    Library,
    Playlist,
    Artist
}

public partial class LibraryTreeViewModel : ObservableObject
{
    private readonly FolderViewModel _folderViewModel;
    private readonly ILibraryService _libraryService;
    private readonly ILogger<LibraryTreeViewModel> _logger;
    private readonly IMusicPlaybackService _playbackService;
    private readonly IUIService _uiService;
    private CancellationTokenSource? _searchCts;

    public LibraryTreeViewModel(
        ILibraryService libraryService,
        IMusicPlaybackService playbackService,
        FolderViewModel folderViewModel,
        IUIService uiService,
        ILogger<LibraryTreeViewModel> logger)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _folderViewModel = folderViewModel;
        _uiService = uiService;
        _logger = logger;
        RootNodes = new ObservableCollection<LibraryTreeNode>();
    }

    public ObservableCollection<LibraryTreeNode> RootNodes { get; }

    [ObservableProperty] public partial LibraryTreeMode Mode { get; set; } = LibraryTreeMode.Library;

    [ObservableProperty] public partial string SearchTerm { get; set; } = string.Empty;

    [ObservableProperty] public partial LibraryTreeNode? SelectedNode { get; set; }

    [ObservableProperty] public partial bool IsLibraryMode { get; set; } = true;

    [ObservableProperty] public partial bool IsPlaylistMode { get; set; }

    [ObservableProperty] public partial bool IsArtistMode { get; set; }

    partial void OnModeChanged(LibraryTreeMode value)
    {
        IsLibraryMode = value == LibraryTreeMode.Library;
        IsPlaylistMode = value == LibraryTreeMode.Playlist;
        IsArtistMode = value == LibraryTreeMode.Artist;
        _ = ReloadAsync();
    }

    partial void OnSearchTermChanged(string value)
    {
        _ = ReloadAsync();
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            IReadOnlyList<LibraryTreeNode> nodes = Mode switch
            {
                LibraryTreeMode.Playlist => await LoadPlaylistsAsync(token).ConfigureAwait(true),
                LibraryTreeMode.Artist => await LoadArtistsAsync(token).ConfigureAwait(true),
                _ => await LoadFoldersAsync(token).ConfigureAwait(true)
            };

            if (token.IsCancellationRequested) return;

            RootNodes.Clear();
            foreach (var node in nodes) RootNodes.Add(node);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload library tree in mode {Mode}", Mode);
        }
    }

    public async Task LoadChildrenAsync(LibraryTreeNode node)
    {
        if (node.IsChildrenLoaded) return;

        try
        {
            if (node.Kind == LibraryTreeNodeKind.Folder)
            {
                var children = (await _libraryService.GetSubFoldersAsync(node.FolderEntityId).ConfigureAwait(true))
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var withChildren = await _libraryService.GetSubFolderIdsWithChildrenAsync(node.FolderEntityId)
                    .ConfigureAwait(true);

                node.Children.Clear();
                foreach (var folder in children)
                    node.Children.Add(CreateFolderNode(folder, node.RootFolderId, isRoot: false,
                        hasChildren: withChildren.Contains(folder.Id)));
            }
            else if (node.Kind == LibraryTreeNodeKind.Artist && node.ArtistId is { } artistId)
            {
                // Expander only when SongCount > 1; children are albums for that album artist.
                var albums = (await _libraryService.GetTopAlbumsForArtistAsync(artistId, 500).ConfigureAwait(true))
                    .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                node.Children.Clear();
                foreach (var album in albums)
                {
                    node.Children.Add(new LibraryTreeNode
                    {
                        Kind = LibraryTreeNodeKind.Album,
                        AlbumId = album.Id,
                        ArtistId = artistId,
                        Name = album.Title,
                        HasUnrealizedChildren = false
                    });
                }
            }
            else
            {
                return;
            }

            node.IsChildrenLoaded = true;
            node.HasUnrealizedChildren = node.Children.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load children for {Name}", node.Name);
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var path = await _uiService.PickSingleFolderAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        await _folderViewModel.AddFolderAndScanCommand.ExecuteAsync(path);
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task PlayRecursiveAsync(LibraryTreeNode? node)
    {
        if (node is null) return;
        try
        {
            var ids = await CollectSongIdsAsync(node, recursive: true).ConfigureAwait(true);
            if (ids.Count == 0) return;
            await _playbackService.PlayAsync(ids, 0, null, ContextFor(node)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Play recursive failed for {Name}", node.Name);
        }
    }

    [RelayCommand]
    private async Task PlayAsync(LibraryTreeNode? node)
    {
        if (node is null) return;
        try
        {
            var ids = await CollectSongIdsAsync(node, recursive: false).ConfigureAwait(true);
            if (ids.Count == 0) return;
            await _playbackService.PlayAsync(ids, 0, null, ContextFor(node)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Play failed for {Name}", node.Name);
        }
    }

    [RelayCommand]
    private async Task AddToQueueAsync(LibraryTreeNode? node)
    {
        if (node is null) return;
        try
        {
            var ids = await CollectSongIdsAsync(node, recursive: false).ConfigureAwait(true);
            if (ids.Count == 0) return;
            await _playbackService.AddRangeToQueueAsync(ids).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add to queue failed for {Name}", node.Name);
        }
    }

    [RelayCommand]
    private async Task AddRecursiveToQueueAsync(LibraryTreeNode? node)
    {
        if (node is null) return;
        try
        {
            var ids = await CollectSongIdsAsync(node, recursive: true).ConfigureAwait(true);
            if (ids.Count == 0) return;
            await _playbackService.AddRangeToQueueAsync(ids).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add recursive to queue failed for {Name}", node.Name);
        }
    }

    [RelayCommand]
    private async Task RescanRootAsync(LibraryTreeNode? node)
    {
        if (node is not { Kind: LibraryTreeNodeKind.Folder } || node.RootFolderId == Guid.Empty) return;
        await _folderViewModel.RescanFolderCommand.ExecuteAsync(node.RootFolderId);
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task DeleteRootAsync(LibraryTreeNode? node)
    {
        if (node is not { Kind: LibraryTreeNodeKind.Folder } || node.RootFolderId == Guid.Empty) return;
        await _folderViewModel.DeleteFolderCommand.ExecuteAsync(node.RootFolderId);
        await ReloadAsync();
    }

    public async Task PlayNodeDropAsync(LibraryTreeNode node, bool append)
    {
        var ids = await CollectSongIdsAsync(node, recursive: true).ConfigureAwait(true);
        if (ids.Count == 0) return;
        if (append)
            await _playbackService.AddRangeToQueueAsync(ids).ConfigureAwait(true);
        else
            await _playbackService.PlayAsync(ids, 0, null, ContextFor(node)).ConfigureAwait(true);
    }

    public async Task<IReadOnlyList<Guid>> CollectSongIdsAsync(LibraryTreeNode node, bool recursive)
    {
        return node.Kind switch
        {
            LibraryTreeNodeKind.Folder when recursive =>
                await _libraryService.GetAllSongIdsInDirectoryRecursiveAsync(
                    node.RootFolderId, node.Path, SongSortOrder.AlbumGroupedAsc).ConfigureAwait(true),
            LibraryTreeNodeKind.Folder =>
                await _libraryService.GetSongIdsInDirectoryAsync(
                    node.RootFolderId, node.Path, SongSortOrder.AlbumGroupedAsc).ConfigureAwait(true),
            LibraryTreeNodeKind.Artist when node.ArtistId is { } artistId =>
                await _libraryService.GetAllSongIdsByArtistIdAsync(artistId, SongSortOrder.AlbumGroupedAsc)
                    .ConfigureAwait(true),
            LibraryTreeNodeKind.Album when node.AlbumId is { } albumId =>
                await _libraryService.GetAllSongIdsByAlbumIdAsync(albumId, SongSortOrder.AlbumGroupedAsc)
                    .ConfigureAwait(true),
            LibraryTreeNodeKind.Playlist when node.PlaylistId is { } playlistId =>
                await _libraryService.GetAllSongIdsByPlaylistIdAsync(playlistId, SongSortOrder.PlaylistOrder)
                    .ConfigureAwait(true),
            LibraryTreeNodeKind.Song when node.SongId is { } songId => new List<Guid> { songId },
            _ => new List<Guid>()
        };
    }

    private async Task<List<LibraryTreeNode>> LoadFoldersAsync(CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var matches = (await _libraryService.SearchFoldersByNamePagedAsync(SearchTerm, 0, 200, token)
                .ConfigureAwait(true)).ToList();
            var nodes = new List<LibraryTreeNode>(matches.Count);
            foreach (var folder in matches)
            {
                var rootId = await ResolveRootFolderIdAsync(folder).ConfigureAwait(true);
                nodes.Add(CreateFolderNode(folder, rootId, folder.ParentFolderId is null, hasChildren: false, searchHit: true));
            }

            nodes.AddRange(await LoadSongTextHitsAsync(SongTextSearchScope.Library, token).ConfigureAwait(true));
            return nodes;
        }

        var roots = (await _libraryService.GetRootFoldersAsync(token).ConfigureAwait(true))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<LibraryTreeNode>();
        foreach (var folder in roots)
        {
            token.ThrowIfCancellationRequested();
            var children = (await _libraryService.GetSubFoldersAsync(folder.Id, token).ConfigureAwait(true))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (children.Count == 0)
            {
                result.Add(CreateFolderNode(folder, folder.Id, isRoot: true, hasChildren: false));
                continue;
            }

            var withChildren = await _libraryService.GetSubFolderIdsWithChildrenAsync(folder.Id, token)
                .ConfigureAwait(true);
            foreach (var child in children)
                result.Add(CreateFolderNode(child, folder.Id, isRoot: false, hasChildren: withChildren.Contains(child.Id)));
        }

        return result;
    }

    private async Task<List<LibraryTreeNode>> LoadPlaylistsAsync(CancellationToken token)
    {
        var playlists = (await _libraryService.GetAllPlaylistsAsync(token).ConfigureAwait(true)).ToList();
        var term = SearchTerm?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            playlists = playlists
                .Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var nodes = playlists
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Playlist,
                Name = p.Name,
                PlaylistId = p.Id,
                HasUnrealizedChildren = false
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
            nodes.AddRange(await LoadSongTextHitsAsync(SongTextSearchScope.Playlist, token).ConfigureAwait(true));

        return nodes;
    }

    private async Task<List<LibraryTreeNode>> LoadArtistsAsync(CancellationToken token)
    {
        IReadOnlyList<(Artist Artist, int SongCount)> rows = !string.IsNullOrWhiteSpace(SearchTerm)
            ? await _libraryService.SearchAlbumArtistsWithSongCountsAsync(SearchTerm, token).ConfigureAwait(true)
            : await _libraryService.GetAlbumArtistsWithSongCountsAsync(token).ConfigureAwait(true);

        var nodes = rows.Select(row => new LibraryTreeNode
        {
            Kind = LibraryTreeNodeKind.Artist,
            Name = string.IsNullOrWhiteSpace(row.Artist.Name) ? Artist.UnknownArtistName : row.Artist.Name,
            ArtistId = row.Artist.Id,
            // Only offer an expander when there is more than one track to drill into (albums).
            HasUnrealizedChildren = row.SongCount > 1
        }).ToList();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
            nodes.AddRange(await LoadSongTextHitsAsync(SongTextSearchScope.AlbumArtist, token).ConfigureAwait(true));

        return nodes;
    }

    private async Task<List<LibraryTreeNode>> LoadSongTextHitsAsync(SongTextSearchScope scope, CancellationToken token)
    {
        if ((SearchTerm?.Trim().Length ?? 0) < 3)
            return [];

        var hits = await _libraryService.SearchSongTextHitsAsync(SearchTerm ?? string.Empty, 200, scope, token).ConfigureAwait(true);
        return hits.Select(ToSongHitNode).ToList();
    }

    private static LibraryTreeNode ToSongHitNode(SongTextHit hit)
    {
        var fileName = Path.GetFileName(hit.FilePath);
        var title = string.IsNullOrWhiteSpace(hit.Title) ? fileName : hit.Title;
        var subtitle = scopeSubtitle(hit, fileName);
        return new LibraryTreeNode
        {
            Kind = LibraryTreeNodeKind.Song,
            SongId = hit.SongId,
            Name = title,
            Subtitle = subtitle,
            RootFolderId = hit.RootFolderId,
            Path = hit.DirectoryPath,
            PlaylistId = hit.PlaylistId,
            ArtistId = hit.ArtistId,
            HasUnrealizedChildren = false
        };

        static string scopeSubtitle(SongTextHit hit, string fileName)
        {
            if (!string.IsNullOrWhiteSpace(hit.PlaylistName))
                return hit.PlaylistName + "  ·  " + fileName;
            if (!string.IsNullOrWhiteSpace(hit.ArtistName) && !string.Equals(hit.ArtistName, hit.Title, StringComparison.Ordinal))
                return hit.ArtistName + "  ·  " + fileName;
            return fileName;
        }
    }

    // The expander shows only for folders that really have subfolders; they load when opened.
    private static LibraryTreeNode CreateFolderNode(Folder folder, Guid rootFolderId, bool isRoot, bool hasChildren,
        bool searchHit = false)
    {
        return new LibraryTreeNode
        {
            Kind = LibraryTreeNodeKind.Folder,
            FolderEntityId = folder.Id,
            RootFolderId = rootFolderId,
            Path = folder.Path,
            Name = folder.Name,
            Subtitle = searchHit ? folder.Path : string.Empty,
            IsLibraryRoot = isRoot,
            HasUnrealizedChildren = hasChildren && !searchHit
        };
    }

    private async Task<Guid> ResolveRootFolderIdAsync(Folder folder)
    {
        var current = folder;
        while (current.ParentFolderId is { } parentId)
        {
            var parent = await _libraryService.GetFolderByIdAsync(parentId).ConfigureAwait(true);
            if (parent is null) break;
            current = parent;
        }

        return current.Id;
    }

    private static PlaybackContext ContextFor(LibraryTreeNode node) =>
        node.Kind switch
        {
            LibraryTreeNodeKind.Artist => new PlaybackContext(PlaybackContextType.Artist, node.ArtistId),
            LibraryTreeNodeKind.Album => new PlaybackContext(PlaybackContextType.Album, node.AlbumId),
            LibraryTreeNodeKind.Playlist => new PlaybackContext(PlaybackContextType.Playlist, node.PlaylistId),
            _ => new PlaybackContext(PlaybackContextType.Folder, node.RootFolderId)
        };
}
