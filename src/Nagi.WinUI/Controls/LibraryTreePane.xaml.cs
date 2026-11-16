using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nagi.Core.Models;
using Nagi.WinUI.Helpers;
using Nagi.WinUI.Models;
using Nagi.WinUI.Resources;
using Nagi.WinUI.Services.Abstractions;
using Nagi.WinUI.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Nagi.WinUI.Controls;

public sealed partial class LibraryTreePane : UserControl
{
    public const string DragFormat = "NagiPortable.LibraryNode";

    public LibraryTreePane()
    {
        InitializeComponent();
    }

    private LibraryTreeViewModel _viewModel = null!;

    public LibraryTreeViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            FolderTree.ItemsSource = value.RootNodes;
        }
    }

    public event EventHandler<LibraryTreeNode>? NodeSelected;

    public event EventHandler<LibraryTreeNode>? NodePlayRequested;

    /// <summary>
    ///     Optional: resolve a song for File info (selected centre-list track, or now playing).
    /// </summary>
    public Func<LibraryTreeNode, Task<Song?>>? ResolveSongForFileInfo { get; set; }

    private void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is LibraryTreeNode node)
            _ = ViewModel.LoadChildrenAsync(node);
    }

    // TreeView reuses item containers and keeps the previous item's state on them. It sets the expander glyph
    // from the container's old HasUnrealizedChildren before the binding runs, and when the binding writes the
    // same value as before, no change reaches the TreeViewNode. Without this, a reused container shows "+" on a
    // folder without subfolders, or no expander on a folder that has them. Sync all three with the new item.
    private void FolderItem_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not TreeViewItem item || args.NewValue is not LibraryTreeNode node) return;

        item.HasUnrealizedChildren = node.HasUnrealizedChildren;
        if (FolderTree.NodeFromContainer(item) is not { } treeNode) return;

        treeNode.HasUnrealizedChildren = node.HasUnrealizedChildren;
        item.GlyphOpacity = treeNode.HasChildren ? 1.0 : 0.0;
    }

    private void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is LibraryTreeNode node)
        {
            ViewModel.SelectedNode = node;
            NodeSelected?.Invoke(this, node);
        }
    }

    private void FolderTree_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is not LibraryTreeNode node)
        {
            e.Cancel = true;
            return;
        }

        e.Data.SetText(Encode(node));
        e.Data.SetData(DragFormat, Encode(node));
        e.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    private void NodeTitle_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTreeNode node })
        {
            NodePlayRequested?.Invoke(this, node);
            e.Handled = true;
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTreeNode node })
            _ = ViewModel.PlayCommand.ExecuteAsync(node);
    }

    private void PlayRecursive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTreeNode node })
            _ = ViewModel.PlayRecursiveCommand.ExecuteAsync(node);
    }

    private void AddToQueue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTreeNode node })
            _ = ViewModel.AddToQueueCommand.ExecuteAsync(node);
    }

    private void AddRecursive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTreeNode node })
            _ = ViewModel.AddRecursiveToQueueCommand.ExecuteAsync(node);
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTreeNode node })
            _ = ViewModel.RescanRootCommand.ExecuteAsync(node);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTreeNode node })
            _ = ViewModel.DeleteRootCommand.ExecuteAsync(node);
    }

    private async void FileInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LibraryTreeNode node }) return;

        Song? song = null;
        if (ResolveSongForFileInfo is not null)
            song = await ResolveSongForFileInfo(node).ConfigureAwait(true);

        var ui = App.Services!.GetRequiredService<IUIService>();
        if (song is null)
        {
            await ui.ShowMessageDialogAsync(
                Strings.Shell_FileInfo_Title,
                "Select a track in the centre list (or play one), then open File info again.")
                .ConfigureAwait(true);
            return;
        }

        await ui.ShowMessageDialogAsync(Strings.Shell_FileInfo_Title, SongFileInfoFormatter.Build(song))
            .ConfigureAwait(true);
    }

    public static string Encode(LibraryTreeNode node)
    {
        return node.Kind switch
        {
            LibraryTreeNodeKind.Artist => $"artist|{node.ArtistId}",
            LibraryTreeNodeKind.Album => $"album|{node.AlbumId}",
            LibraryTreeNodeKind.Playlist => $"playlist|{node.PlaylistId}",
            _ => $"folder|{node.RootFolderId}|{node.Path}"
        };
    }

    public static LibraryTreeNode? Decode(string payload)
    {
        var parts = payload.Split('|', 3);
        if (parts.Length < 2) return null;
        return parts[0] switch
        {
            "artist" when Guid.TryParse(parts[1], out var artistId) => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Artist,
                ArtistId = artistId,
                Name = artistId.ToString()
            },
            "album" when Guid.TryParse(parts[1], out var albumId) => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Album,
                AlbumId = albumId,
                Name = albumId.ToString()
            },
            "playlist" when Guid.TryParse(parts[1], out var playlistId) => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Playlist,
                PlaylistId = playlistId,
                Name = playlistId.ToString()
            },
            "folder" when parts.Length == 3 && Guid.TryParse(parts[1], out var rootId) => new LibraryTreeNode
            {
                Kind = LibraryTreeNodeKind.Folder,
                RootFolderId = rootId,
                Path = parts[2],
                Name = parts[2]
            },
            _ => null
        };
    }
}
