using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nagi.Core.Constants;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Data;
using Nagi.WinUI.Controls;
using Nagi.WinUI.Helpers;
using Nagi.WinUI.Models;
using Nagi.WinUI.Pages;
using Nagi.WinUI.Services.Abstractions;
using Nagi.WinUI.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Nagi.WinUI;

public sealed partial class MainPage : UserControl
{
    private const double VolumeChangeStep = 5.0;
    private readonly ILogger<MainPage> _logger;
    private readonly INavigationService _navigationService;
    private readonly IUISettingsService _settingsService;
    private readonly ILibraryScanner _libraryScanner;
    private readonly IDispatcherService _dispatcherService;
    private readonly Debouncer _libraryRefresh = new(TimeSpan.FromMilliseconds(500));
    private bool _isUnloaded;

    public MainPage()
    {
        ViewModel = App.Services!.GetRequiredService<PlayerViewModel>();
        Tree = App.Services!.GetRequiredService<LibraryTreeViewModel>();
        Browse = App.Services!.GetRequiredService<BrowseSongListViewModel>();
        Settings = App.Services!.GetRequiredService<SettingsViewModel>();
        _navigationService = App.Services!.GetRequiredService<INavigationService>();
        _settingsService = App.Services!.GetRequiredService<IUISettingsService>();
        _libraryScanner = App.Services!.GetRequiredService<ILibraryScanner>();
        _dispatcherService = App.Services!.GetRequiredService<IDispatcherService>();
        _logger = App.Services!.GetRequiredService<ILogger<MainPage>>();

        InitializeComponent();
        DataContext = ViewModel;
        InitializeWindowChrome();

        TreePane.ViewModel = Tree;
        TreePane.ResolveSongForFileInfo = ResolveSongForTreeFileInfoAsync;
        ViewModel.ExpandQueueForTransport = ExpandQueueFromBrowseAsync;
        SongList.ViewModel = Browse;
        SongList.TreeViewModel = Tree;
        EqStrip.Settings = Settings;
        OrderBar.ViewModel = ViewModel;
        StatusStrip.ViewModel = ViewModel;

        _navigationService.Initialize(ContentFrame);
        ContentFrame.Navigated += OnContentFrameNavigated;

        Loaded += OnMainPageLoaded;
        Unloaded += OnMainPageUnloaded;
    }

    public PlayerViewModel ViewModel { get; }
    public LibraryTreeViewModel Tree { get; }
    public BrowseSongListViewModel Browse { get; }
    public SettingsViewModel Settings { get; }

    public void UpdateActivationVisualState(WindowActivationState activationState)
    {
        SetTitleBarActive(activationState != WindowActivationState.Deactivated);
    }

    private async void OnMainPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnMainPageLoaded;
        _libraryScanner.LibraryContentChanged += OnLibraryContentChanged;
        if (App.RootWindow is MainWindow mainWindow)
            mainWindow.ApplyShellMinimumSize();

        try
        {
            await RestoreBrowseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore shell browse state.");
            try
            {
                await Tree.ReloadAsync();
            }
            catch (Exception reloadEx)
            {
                _logger.LogError(reloadEx, "Failed to load library tree.");
            }
        }
    }

    private async Task RestoreBrowseAsync()
    {
        var state = await _settingsService.GetShellBrowseStateAsync();
        if (state is not null &&
            Enum.TryParse<LibraryTreeMode>(state.Mode, out var mode) &&
            Tree.Mode != mode)
        {
            Tree.Mode = mode;
        }

        // Always await a fresh tree load; cancels any reload started by Mode change.
        await Tree.ReloadAsync();
        UpdateModeTabs();

        if (state is null) return;

        var node = ToNode(state);
        if (node is null) return;

        Tree.SelectedNode = node;
        await LoadBrowseAsync(node, persist: false);
    }

    private static LibraryTreeNode? ToNode(ShellBrowseState state)
    {
        if (!Enum.TryParse<LibraryTreeNodeKind>(state.Kind, out var kind))
            return null;

        return kind switch
        {
            LibraryTreeNodeKind.Playlist when state.EntityId != Guid.Empty => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Playlist,
                PlaylistId = state.EntityId,
                Name = state.Name
            },
            LibraryTreeNodeKind.Artist when state.EntityId != Guid.Empty => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Artist,
                ArtistId = state.EntityId,
                Name = state.Name
            },
            LibraryTreeNodeKind.Album when state.EntityId != Guid.Empty => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Album,
                AlbumId = state.EntityId,
                Name = state.Name
            },
            LibraryTreeNodeKind.Folder when state.RootFolderId != Guid.Empty => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Folder,
                RootFolderId = state.RootFolderId,
                Path = state.Path,
                Name = string.IsNullOrWhiteSpace(state.Name) ? state.Path : state.Name
            },
            _ => null
        };
    }

    private async Task LoadSongHitAsync(LibraryTreeNode node)
    {
        if (Tree.IsPlaylistMode && node.PlaylistId is { } playlistId)
        {
            await Browse.LoadPlaylistAsync(playlistId, node.Name);
            return;
        }

        if (Tree.IsArtistMode && node.ArtistId is { } artistId)
        {
            var header = node.Subtitle?.Split("  ·  ", 2, StringSplitOptions.TrimEntries) is { Length: 2 } parts
                ? parts[0]
                : node.Name;
            await Browse.LoadArtistAsync(artistId, header);
            return;
        }

        await Browse.LoadFolderAsync(node.RootFolderId, node.Path, node.Name);
    }

    /// <summary>
    ///     A song hit restores as the folder, playlist, or artist that was opened from it.
    /// </summary>
    private LibraryTreeNode BrowseStateNode(LibraryTreeNode node)
    {
        if (node.Kind != LibraryTreeNodeKind.Song)
            return node;

        if (Tree.IsPlaylistMode && node.PlaylistId is { } playlistId)
        {
            return new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Playlist,
                PlaylistId = playlistId,
                Name = node.Name
            };
        }

        if (Tree.IsArtistMode && node.ArtistId is { } artistId)
        {
            return new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Artist,
                ArtistId = artistId,
                Name = node.Name
            };
        }

        return new LibraryTreeNode
        {
            Kind = LibraryTreeNodeKind.Folder,
            RootFolderId = node.RootFolderId,
            Path = node.Path,
            Name = node.Name
        };
    }

    private static ShellBrowseState FromNode(LibraryTreeMode mode, LibraryTreeNode node)
    {
        return new ShellBrowseState
        {
            Mode = mode.ToString(),
            Kind = node.Kind.ToString(),
            RootFolderId = node.RootFolderId,
            EntityId = node.Kind switch
            {
                LibraryTreeNodeKind.Playlist => node.PlaylistId ?? Guid.Empty,
                LibraryTreeNodeKind.Artist => node.ArtistId ?? Guid.Empty,
                LibraryTreeNodeKind.Album => node.AlbumId ?? Guid.Empty,
                _ => node.FolderEntityId
            },
            Path = node.Path,
            Name = node.Name
        };
    }

    private void OnMainPageUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _libraryScanner.LibraryContentChanged -= OnLibraryContentChanged;
        _libraryRefresh.Dispose();
        ContentFrame.Navigated -= OnContentFrameNavigated;
        Unloaded -= OnMainPageUnloaded;
    }

    private void OnLibraryContentChanged(object? sender, LibraryContentChangedEventArgs e)
    {
        _libraryRefresh.Trigger(async token =>
        {
            try
            {
                await _dispatcherService.EnqueueAsync(async () =>
                {
                    if (_isUnloaded || token.IsCancellationRequested) return;
                    await Tree.RefreshAsync();
                    if (_isUnloaded || token.IsCancellationRequested) return;
                    if (Tree.SelectedNode is { } node)
                        await LoadBrowseAsync(node, persist: false, scrollToSelection: false);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh the shell after a library change.");
            }
        });
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowOverlay();
        _navigationService.Navigate(typeof(SettingsPage));
    }

    private async void NowPlayingFileInfo_Click(object sender, RoutedEventArgs e)
    {
        var song = ViewModel.CurrentPlayingTrack;
        if (song is null) return;

        var ui = App.Services!.GetRequiredService<IUIService>();
        await ui.ShowMessageDialogAsync(
            Nagi.WinUI.Resources.Strings.Shell_FileInfo_Title,
            SongFileInfoFormatter.Build(song));
    }

    private async Task<Song?> ResolveSongForTreeFileInfoAsync(LibraryTreeNode node)
    {
        // Prefer the track selected / highlighted in the centre list.
        if (SongList.SelectedSong is { } selected)
            return selected;

        // Else the now-playing track when it belongs to this node.
        var playing = ViewModel.CurrentPlayingTrack;
        if (playing is null) return null;

        var ids = await Tree.CollectSongIdsAsync(node, recursive: true).ConfigureAwait(true);
        return ids.Contains(playing.Id) ? playing : null;
    }

    private void LyricsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLyricsPageActive)
        {
            CloseOverlay();
            return;
        }

        OpenLyricsPage();
    }

    private void LyricsPanel_OpenRequested(object? sender, EventArgs e)
    {
        if (!ViewModel.IsLyricsPageActive) OpenLyricsPage();
    }

    private void OpenLyricsPage()
    {
        ShowOverlay();
        _navigationService.Navigate(typeof(LyricsPage));
    }

    // Below this the lyrics panel stops shrinking and the left column scrolls instead.
    private const double LyricsPanelMinHeight = 96;

    /// <summary>Gives the lyrics panel whatever height the controls above it leave free.</summary>
    private void LeftPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var free = LeftPaneScroller.ActualHeight - LeftPaneStack.ActualHeight - LeftPaneGrid.RowSpacing;
        LyricsPanel.Height = Math.Max(LyricsPanelMinHeight, Math.Floor(free));
    }

    private void OverlayBack_Click(object sender, RoutedEventArgs e)
    {
        if (_navigationService.CanGoBack)
            _navigationService.GoBack();
        else
            CloseOverlay();
    }

    private void ShowOverlay()
    {
        OverlayHost.Visibility = Visibility.Visible;
    }

    private void CloseOverlay()
    {
        _navigationService.Reset();
        OverlayHost.Visibility = Visibility.Collapsed;
        ViewModel.IsLyricsPageActive = false;
        // The lyrics may have been edited, removed or fetched in the overlay.
        LyricsPanel.Refresh();
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (_isUnloaded) return;
        if (ContentFrame.Content is null)
        {
            OverlayHost.Visibility = Visibility.Collapsed;
            ViewModel.IsLyricsPageActive = false;
            LyricsPanel.Refresh();
            return;
        }

        OverlayHost.Visibility = Visibility.Visible;
        ViewModel.IsLyricsPageActive = e.SourcePageType == typeof(LyricsPage);
    }

    private void ModeLibrary_Click(object sender, RoutedEventArgs e)
    {
        Tree.Mode = LibraryTreeMode.Library;
        UpdateModeTabs();
    }

    private void ModePlaylist_Click(object sender, RoutedEventArgs e)
    {
        Tree.Mode = LibraryTreeMode.Playlist;
        UpdateModeTabs();
    }

    private void ModeArtist_Click(object sender, RoutedEventArgs e)
    {
        Tree.Mode = LibraryTreeMode.Artist;
        UpdateModeTabs();
    }

    private void UpdateModeTabs()
    {
        LibraryTab.Style = TabStyle(Tree.IsLibraryMode);
        PlaylistTab.Style = TabStyle(Tree.IsPlaylistMode);
        ArtistTab.Style = TabStyle(Tree.IsArtistMode);
    }

    private static Style TabStyle(bool selected)
    {
        return (Style)Application.Current.Resources[selected ? "ShellTabButtonSelectedStyle" : "ShellTabButtonStyle"];
    }

    private void TreePane_NodeSelected(object? sender, LibraryTreeNode node)
    {
        _ = LoadBrowseAsync(node);
    }

    private void TreePane_NodePlayRequested(object? sender, LibraryTreeNode node)
    {
        _ = Tree.PlayRecursiveCommand.ExecuteAsync(node);
    }

    private async Task LoadBrowseAsync(LibraryTreeNode node, bool persist = true, bool scrollToSelection = true)
    {
        try
        {
            switch (node.Kind)
            {
                case LibraryTreeNodeKind.Playlist when node.PlaylistId is { } playlistId:
                    await Browse.LoadPlaylistAsync(playlistId, node.Name);
                    break;
                case LibraryTreeNodeKind.Artist when node.ArtistId is { } artistId:
                    await Browse.LoadArtistAsync(artistId, node.Name);
                    break;
                case LibraryTreeNodeKind.Album when node.AlbumId is { } albumId:
                    await Browse.LoadAlbumAsync(albumId, node.Name);
                    break;
                case LibraryTreeNodeKind.Song:
                    await LoadSongHitAsync(node);
                    break;
                default:
                    await Browse.LoadFolderAsync(node.RootFolderId, node.Path, node.Name);
                    break;
            }

            if (persist)
                await _settingsService.SetShellBrowseStateAsync(FromNode(Tree.Mode, BrowseStateNode(node)));

            if (scrollToSelection)
            {
                if (node.Kind == LibraryTreeNodeKind.Song && node.SongId is { } songId)
                    SongList.ScrollToSong(songId);
                else
                    SongList.ScrollToNowPlaying();
            }
            if (ViewModel.CurrentPlayingTrack is null)
                ViewModel.SetIdlePreview(Browse.FirstSong);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fill browse list for {Name}", node.Name);
        }
    }

    private async void SongList_SongPlayRequested(object? sender, BrowseSongRow row)
    {
        var playback = App.Services!.GetRequiredService<IMusicPlaybackService>();
        var (ids, index) = await ResolveBrowseQueueAsync(row.Song.Id).ConfigureAwait(true);
        if (ids.Count == 0)
        {
            await playback.PlayAsync(row.Song).ConfigureAwait(true);
            return;
        }

        await playback.PlayAsync(ids, index).ConfigureAwait(true);
    }

    /// <summary>
    ///     Next/Previous on a one-track queue jump through the album currently shown in the centre list.
    /// </summary>
    private async Task<bool> ExpandQueueFromBrowseAsync(bool forward)
    {
        var currentId = ViewModel.CurrentPlayingTrack?.Id ?? Browse.FirstSong?.Id;
        if (currentId is null) return false;

        var (ids, index) = await ResolveBrowseQueueAsync(currentId.Value).ConfigureAwait(true);
        if (ids.Count <= 1) return false;

        var target = forward ? index + 1 : index - 1;
        if (ViewModel.CurrentPlayingTrack is null)
            target = 0;
        if (target < 0 || target >= ids.Count) return false;

        var playback = App.Services!.GetRequiredService<IMusicPlaybackService>();
        await playback.PlayAsync(ids, target).ConfigureAwait(true);
        return true;
    }

    private async Task<(IReadOnlyList<Guid> Ids, int Index)> ResolveBrowseQueueAsync(Guid songId)
    {
        if (Tree.SelectedNode is { } node)
        {
            var recursive = node.Kind == LibraryTreeNodeKind.Folder;
            var ids = (await Tree.CollectSongIdsAsync(node, recursive).ConfigureAwait(true)).ToList();
            var index = ids.FindIndex(id => id == songId);
            if (ids.Count > 0)
                return (ids, index >= 0 ? index : 0);
        }

        var visible = Browse.VisibleSongIds.ToList();
        var visibleIndex = visible.FindIndex(id => id == songId);
        return (visible, visibleIndex >= 0 ? visibleIndex : 0);
    }

    private void QueueFlyout_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) ||
            e.DataView.Contains(LibraryTreePane.DragFormat))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private void QueueButton_DragEnter(object sender, DragEventArgs e)
    {
        QueueFlyout_DragOver(sender, e);
        if (e.AcceptedOperation == DataPackageOperation.Copy && sender is Button button)
            button.Flyout?.ShowAt(button);
    }

    private async void QueueFlyout_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(LibraryTreePane.DragFormat) ||
                e.DataView.Contains(StandardDataFormats.Text))
            {
                string? payload = null;
                if (e.DataView.Contains(LibraryTreePane.DragFormat))
                    payload = await e.DataView.GetDataAsync(LibraryTreePane.DragFormat) as string;
                payload ??= await e.DataView.GetTextAsync();
                if (LibraryTreePane.Decode(payload ?? string.Empty) is { } node)
                {
                    await Tree.PlayNodeDropAsync(node, append: true);
                    return;
                }
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            var paths = new List<string>();
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                await CollectMusicFilesAsync(item, paths);

            await ViewModel.AddFilesToQueueAsync(paths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add dropped files to the playback queue.");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task CollectMusicFilesAsync(IStorageItem item, List<string> paths)
    {
        if (item is StorageFile file)
        {
            if (FileExtensions.MusicFileExtensions.Contains(file.FileType)) paths.Add(file.Path);
            return;
        }

        if (item is not StorageFolder folder) return;

        try
        {
            var children = await folder.GetItemsAsync();
            foreach (var child in children.OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase))
                await CollectMusicFilesAsync(child, paths);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read dropped folder {FolderPath}.", folder.Path);
        }
    }

    private void MediaSeekerSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsUserDraggingSlider = true;
    }

    private void MediaSeekerSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsUserDraggingSlider = false;
    }

    private void MediaSeekerSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsUserDraggingSlider = false;
    }

    private void MediaSeekerSlider_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Home
            or VirtualKey.End)
        {
            ViewModel.IsUserDraggingSlider = true;
        }
    }

    private void MediaSeekerSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Home
            or VirtualKey.End)
        {
            ViewModel.IsUserDraggingSlider = false;
        }
    }

    private void VolumeSliderContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        if (delta > 0)
            ViewModel.CurrentVolume = Math.Min(100, ViewModel.CurrentVolume + VolumeChangeStep);
        else if (delta < 0)
            ViewModel.CurrentVolume = Math.Max(0, ViewModel.CurrentVolume - VolumeChangeStep);
        e.Handled = true;
    }
}
