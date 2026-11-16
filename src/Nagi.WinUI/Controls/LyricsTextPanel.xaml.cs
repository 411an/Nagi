using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;

namespace Nagi.WinUI.Controls;

/// <summary>
///     The lyrics of the playing track as plain text to read along, without timing. The text comes
///     from the track's lyrics file or, without one, from its lyrics tag (see
///     <see cref="ILyricsTextService" />); the panel never goes online. A click asks the page to open
///     the full lyrics view.
/// </summary>
public sealed partial class LyricsTextPanel : UserControl
{
    private CancellationTokenSource? _loadCts;
    private ILogger<LyricsTextPanel>? _logger;
    private ILyricsTextService? _lyricsText;
    private IMusicPlaybackService? _playback;
    private Guid? _shownSongId;

    public LyricsTextPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Raised when the panel is clicked.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Reads the lyrics of the playing track again, for example after they were edited.</summary>
    public void Refresh() => _ = LoadAsync();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _playback ??= App.Services?.GetService<IMusicPlaybackService>();
        _lyricsText ??= App.Services?.GetService<ILyricsTextService>();
        _logger ??= App.Services?.GetService<ILogger<LyricsTextPanel>>();
        if (_playback is not null) _playback.TrackChanged += OnTrackChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_playback is not null) _playback.TrackChanged -= OnTrackChanged;
        _loadCts?.Cancel();
    }

    private void OnTrackChanged() => DispatcherQueue.TryEnqueue(Refresh);

    private async Task LoadAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var song = _playback?.CurrentTrack;

        string? text = null;
        if (song is not null && _lyricsText is not null)
        {
            try
            {
                // Looking for a lyrics file lists the track's folder, so keep it off the UI thread.
                text = await Task.Run(() => _lyricsText.GetLyricsTextAsync(song, cts.Token), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not read the lyrics of {FilePath}", song.FilePath);
            }
        }

        if (cts.IsCancellationRequested) return;
        Show(song, text);
    }

    private void Show(Song? song, string? text)
    {
        LyricsText.Text = text ?? string.Empty;
        NoLyricsText.Visibility = song is not null && text is null ? Visibility.Visible : Visibility.Collapsed;

        // A new track starts at the top; the same track keeps its place when the text is read again.
        if (song?.Id != _shownSongId) Scroller.ChangeView(null, 0, null, true);
        _shownSongId = song?.Id;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
}
