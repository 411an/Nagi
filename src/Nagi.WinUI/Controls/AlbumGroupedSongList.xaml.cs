using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nagi.Core.Models;
using Nagi.WinUI.Helpers;
using Nagi.WinUI.Models;
using Nagi.WinUI.Resources;
using Nagi.WinUI.Services.Abstractions;
using Nagi.WinUI.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Nagi.WinUI.Controls;

public sealed partial class AlbumGroupedSongList : UserControl
{
    private BrowseSongListViewModel _viewModel = null!;

    public AlbumGroupedSongList()
    {
        InitializeComponent();
    }

    public BrowseSongListViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            SongListView.ItemsSource = value.Items;
        }
    }

    public LibraryTreeViewModel? TreeViewModel { get; set; }

    public event EventHandler<BrowseSongRow>? SongPlayRequested;

    /// <summary>Song selected in the centre list (for File info from the library tree).</summary>
    public Song? SelectedSong =>
        SongListView.SelectedItem is BrowseSongRow row ? row.Song : null;

    /// <summary>
    ///     Scrolls the list so the now-playing row is on screen (not necessarily centered).
    /// </summary>
    public void ScrollToNowPlaying() => ScrollToRow(ViewModel?.FindNowPlayingRow());

    public void ScrollToSong(Guid songId) => ScrollToRow(ViewModel?.FindSongRow(songId));

    private void ScrollToRow(BrowseSongRow? row)
    {
        if (row is null) return;

        SongListView.SelectedItem = row;

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            SongListView.UpdateLayout();
            SongListView.ScrollIntoView(row, ScrollIntoViewAlignment.Default);
        });
    }

    private void Songs_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element != SongListView)
        {
            if (element is FrameworkElement { DataContext: BrowseSongRow row })
            {
                SongPlayRequested?.Invoke(this, row);
                e.Handled = true;
                return;
            }

            if (element is FrameworkElement { DataContext: BrowseAlbumHeader })
                return;

            element = VisualTreeHelper.GetParent(element);
        }
    }

    private async void FileInfo_Click(object sender, RoutedEventArgs e)
    {
        Song? song = null;
        if (sender is FrameworkElement { DataContext: BrowseSongRow row })
            song = row.Song;
        song ??= SelectedSong;
        if (song is null) return;

        var ui = App.Services!.GetRequiredService<IUIService>();
        await ui.ShowMessageDialogAsync(Strings.Shell_FileInfo_Title, SongFileInfoFormatter.Build(song));
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(LibraryTreePane.DragFormat) ||
            e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            if (TreeViewModel is not null && e.DataView.Contains(LibraryTreePane.DragFormat))
            {
                if (await e.DataView.GetDataAsync(LibraryTreePane.DragFormat) is string payload &&
                    LibraryTreePane.Decode(payload) is { } node)
                {
                    await TreeViewModel.PlayNodeDropAsync(node, append: true);
                }
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
