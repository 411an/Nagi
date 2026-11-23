using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Data;
using Nagi.WinUI.Helpers;
using Nagi.WinUI.Resources;

namespace Nagi.WinUI.ViewModels;

public abstract class BrowseListItem : ObservableObject
{
}

public partial class BrowseAlbumHeader : BrowseListItem
{
    public BrowseAlbumHeader(string title, string subtitle, string? coverUri, int trackCount)
    {
        Title = title;
        Subtitle = subtitle;
        CoverUri = coverUri;
        TrackCountText = string.Format(Strings.Shell_TracksCount, trackCount);
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string? CoverUri { get; }
    public string TrackCountText { get; }
}

public partial class BrowseSongRow : BrowseListItem
{
    public BrowseSongRow(Song song, bool showFilename)
    {
        Song = song;
        Title = song.Title;
        FileName = Path.GetFileNameWithoutExtension(song.FilePath);
        TrackNumber = song.TrackNumber;
        DurationText = FormatDuration(song.Duration);
        AlbumId = song.AlbumId;
        AlbumTitle = song.Album?.Title ?? string.Empty;
        ShowFilename = showFilename;
    }

    public Song Song { get; }
    public string Title { get; }
    public string FileName { get; }
    public int? TrackNumber { get; }
    public string TrackNumberText => TrackNumber?.ToString() ?? string.Empty;
    public string DurationText { get; }
    public Guid? AlbumId { get; }
    public string AlbumTitle { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial bool ShowFilename { get; set; }

    [ObservableProperty] public partial bool IsNowPlaying { get; set; }

    public string DisplayTitle => Song.IsCueTrack ? Title : ShowFilename ? FileName : Title;

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }
}

public partial class BrowseSongListViewModel : ObservableObject
{
    public const int PageSize = 200;
    private readonly ILibraryReader _libraryReader;
    private readonly ILogger<BrowseSongListViewModel> _logger;
    private readonly PlayerViewModel _player;
    private CancellationTokenSource? _loadCts;

    public BrowseSongListViewModel(
        ILibraryReader libraryReader,
        PlayerViewModel player,
        ILogger<BrowseSongListViewModel> logger)
    {
        _libraryReader = libraryReader;
        _player = player;
        _logger = logger;
        Items = new ObservableRangeCollection<BrowseListItem>();
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    public ObservableRangeCollection<BrowseListItem> Items { get; }

    public BrowseSongRow? FindNowPlayingRow()
    {
        var playingId = _player.CurrentPlayingTrack?.Id;
        if (playingId is null) return null;
        return Items.OfType<BrowseSongRow>().FirstOrDefault(r => r.Song.Id == playingId);
    }

    public Song? FirstSong => Items.OfType<BrowseSongRow>().FirstOrDefault()?.Song;

    public BrowseSongRow? FindSongRow(Guid songId) =>
        Items.OfType<BrowseSongRow>().FirstOrDefault(r => r.Song.Id == songId);

    public IReadOnlyList<Guid> VisibleSongIds =>
        Items.OfType<BrowseSongRow>().Select(r => r.Song.Id).ToList();

    [ObservableProperty] public partial int TotalCount { get; set; }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial string HeaderText { get; set; } = string.Empty;

    [ObservableProperty] public partial string HeaderSubtitle { get; set; } = string.Empty;

    public async Task LoadFolderAsync(Guid rootFolderId, string directoryPath, string displayName)
    {
        await LoadAsync(displayName, groupByAlbum: true, () =>
            _libraryReader.GetSongsInDirectoryRecursiveOffsetAsync(
                rootFolderId, directoryPath, 0, PageSize, SongSortOrder.AlbumGroupedAsc));
    }

    public async Task LoadPlaylistAsync(Guid playlistId, string displayName)
    {
        await LoadAsync(displayName, groupByAlbum: false, () =>
            _libraryReader.GetSongsByPlaylistPagedAsync(
                playlistId, 1, PageSize, SongSortOrder.PlaylistOrder));
    }

    public async Task LoadArtistAsync(Guid artistId, string displayName)
    {
        await LoadAsync(displayName, groupByAlbum: true, () =>
            _libraryReader.GetSongsByArtistIdPagedAsync(
                artistId, 1, PageSize, SongSortOrder.AlbumGroupedAsc));
    }

    public async Task LoadAlbumAsync(Guid albumId, string displayName)
    {
        await LoadAsync(displayName, groupByAlbum: false, () =>
            _libraryReader.GetSongsByAlbumIdPagedAsync(
                albumId, 1, PageSize, SongSortOrder.TrackNumberAsc));
    }

    public void Clear()
    {
        _loadCts?.Cancel();
        Items.Clear();
        TotalCount = 0;
        HeaderText = string.Empty;
        HeaderSubtitle = string.Empty;
    }

    private async Task LoadAsync(string displayName, bool groupByAlbum, Func<Task<PagedResult<Song>>> fetch)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        IsLoading = true;
        HeaderText = displayName;

        try
        {
            var page = await fetch().ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            TotalCount = page.TotalCount;
            HeaderSubtitle = string.Format(Strings.Shell_TracksCount, page.TotalCount);
            Items.ReplaceRange(BuildItems(page.Items, groupByAlbum));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load browse list for {Name}", displayName);
            Items.Clear();
            TotalCount = 0;
        }
        finally
        {
            if (!token.IsCancellationRequested) IsLoading = false;
        }
    }

    private IReadOnlyList<BrowseListItem> BuildItems(IEnumerable<Song> songs, bool groupByAlbum)
    {
        var songList = songs.ToList();
        var showFilename = _player.ShowFilenameInList;
        var playingId = _player.CurrentPlayingTrack?.Id;
        var items = new List<BrowseListItem>();

        var albumDirectories = songList
            .GroupBy(song => song.AlbumId ?? Guid.Empty)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(song => song.DirectoryPath ?? string.Empty)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        Guid? currentAlbum = null;
        string? currentDirectory = null;
        var albumSongs = new List<Song>();

        void FlushAlbum()
        {
            if (albumSongs.Count == 0) return;
            var first = albumSongs[0];
            var title = first.Album?.Title ?? string.Empty;
            var albumId = first.AlbumId ?? Guid.Empty;
            if (albumDirectories.TryGetValue(albumId, out var directories) && directories.Count > 1)
            {
                var directory = first.DirectoryPath ?? string.Empty;
                var folderName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(folderName)) folderName = directory;

                var hasDuplicateFolderName = directories
                    .Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                    .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1 && string.Equals(group.Key, folderName, StringComparison.OrdinalIgnoreCase));
                title += $" ({(hasDuplicateFolderName ? directory : folderName)})";
            }
            var year = first.Album?.Year ?? first.Year;
            var artist = first.Album?.ArtistName ?? first.ArtistName;
            var subtitleParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(artist)) subtitleParts.Add(artist);
            if (year is > 0) subtitleParts.Add(year.Value.ToString());
            items.Add(new BrowseAlbumHeader(
                title,
                string.Join("  ·  ", subtitleParts),
                ImageUriHelper.GetUriWithCacheBuster(first.Album?.CoverArtUri ?? first.AlbumArtUriFromTrack),
                albumSongs.Count));
            foreach (var song in albumSongs)
            {
                items.Add(new BrowseSongRow(song, showFilename)
                {
                    IsNowPlaying = playingId is { } id && song.Id == id
                });
            }

            albumSongs.Clear();
        }

        foreach (var song in songList)
        {
            if (groupByAlbum)
            {
                var albumId = song.AlbumId ?? Guid.Empty;
                var directory = song.DirectoryPath ?? string.Empty;
                if (currentAlbum != albumId || !string.Equals(currentDirectory, directory, StringComparison.OrdinalIgnoreCase))
                {
                    FlushAlbum();
                    currentAlbum = albumId;
                    currentDirectory = directory;
                }

                albumSongs.Add(song);
            }
            else
            {
                items.Add(new BrowseSongRow(song, showFilename)
                {
                    IsNowPlaying = playingId is { } id && song.Id == id
                });
            }
        }

        if (groupByAlbum) FlushAlbum();
        return items;
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.ShowFilenameInList))
        {
            foreach (var item in Items)
            {
                if (item is BrowseSongRow row)
                    row.ShowFilename = _player.ShowFilenameInList;
            }
        }
        else if (e.PropertyName == nameof(PlayerViewModel.CurrentPlayingTrack))
        {
            var playingId = _player.CurrentPlayingTrack?.Id;
            foreach (var item in Items)
            {
                if (item is BrowseSongRow row)
                    row.IsNowPlaying = playingId is { } id && row.Song.Id == id;
            }
        }
    }
}
