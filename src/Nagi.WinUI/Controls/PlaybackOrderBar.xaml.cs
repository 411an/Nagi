using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nagi.WinUI.ViewModels;

namespace Nagi.WinUI.Controls;

public sealed partial class PlaybackOrderBar : UserControl
{
    private PlayerViewModel? _viewModel;

    public PlaybackOrderBar()
    {
        InitializeComponent();
    }

    public PlayerViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = value;
            if (value != null)
            {
                value.PropertyChanged += OnViewModelPropertyChanged;
                Refresh();
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentPlaybackOrder)
            or nameof(PlayerViewModel.IsShuffleEnabled)
            or nameof(PlayerViewModel.CurrentRepeatMode))
            Refresh();
    }

    private void Refresh()
    {
        if (_viewModel is null) return;
        var order = _viewModel.CurrentPlaybackOrder;
        Apply(DefaultButton, order == ShellPlaybackOrder.Default);
        Apply(RepeatPlaylistButton, order == ShellPlaybackOrder.RepeatPlaylist);
        Apply(RepeatTrackButton, order == ShellPlaybackOrder.RepeatTrack);
        Apply(ShuffleTracksButton, order == ShellPlaybackOrder.ShuffleTracks);
        Apply(ShuffleAlbumsButton, order == ShellPlaybackOrder.ShuffleAlbums);
        Apply(RandomButton, order == ShellPlaybackOrder.Random);
    }

    private static void Apply(Button button, bool active)
    {
        button.Style = (Style)Application.Current.Resources[active ? "ShellOrderButtonActiveStyle" : "ShellOrderButtonStyle"];
    }

    private void Default_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.SetPlaybackOrder(ShellPlaybackOrder.Default);

    private void RepeatPlaylist_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.SetPlaybackOrder(ShellPlaybackOrder.RepeatPlaylist);

    private void RepeatTrack_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.SetPlaybackOrder(ShellPlaybackOrder.RepeatTrack);

    private void ShuffleTracks_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.SetPlaybackOrder(ShellPlaybackOrder.ShuffleTracks);

    private void ShuffleAlbums_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.SetPlaybackOrder(ShellPlaybackOrder.ShuffleAlbums);

    private void Random_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.SetPlaybackOrder(ShellPlaybackOrder.Random);

    private void Filename_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.ShowFilenameInList = !_viewModel.ShowFilenameInList;
    }
}
