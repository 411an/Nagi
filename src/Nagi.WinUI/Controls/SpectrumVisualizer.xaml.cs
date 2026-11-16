using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Implementations;

namespace Nagi.WinUI.Controls;

/// <summary>
///     Bars showing the real spectrum of the playing track. FFmpeg decodes the track in the
///     background (see <see cref="ITrackSpectrumService" />) and the bars follow the playback
///     position. Without FFmpeg, or for a file FFmpeg cannot read, the bars fall back to a
///     decorative animation.
/// </summary>
public sealed partial class SpectrumVisualizer : UserControl
{
    public const int BarCount = SpectrumAnalyzer.BandCount;

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(SpectrumVisualizer),
        new PropertyMetadata(false, OnPlayingChanged));

    // A silent bar stays a thin line instead of vanishing.
    private const double MinScale = 0.04;

    // Bars jump up at once and fall at this many bar heights per second.
    private const double FallPerSecond = 1.8;

    // How far the position may run ahead of the player's last report before waiting for it.
    private static readonly TimeSpan MaxExtrapolation = TimeSpan.FromSeconds(1);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ScaleTransform[] _scales = new ScaleTransform[BarCount];
    private readonly double[] _shown = new double[BarCount];
    private readonly float[] _target = new float[BarCount];

    private Storyboard? _fallback;
    private FallbackState _fallbackState;
    private bool _isLoaded;
    private TimeSpan _lastTick;
    private IMusicPlaybackService? _playback;
    private bool _rendering;
    private TimeSpan _reportedAt;
    private TimeSpan? _reportedPosition;
    private Song? _song;
    private TrackSpectrum? _spectrum;
    private ITrackSpectrumService? _spectrumService;

    public SpectrumVisualizer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Update());
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    private static void OnPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumVisualizer visualizer)
        {
            visualizer._reportedPosition = null;
            visualizer.Update();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _playback ??= App.Services?.GetService<IMusicPlaybackService>();
        _spectrumService ??= App.Services?.GetService<ITrackSpectrumService>();
        if (BarsHost.Children.Count == 0) CreateBars();
        _isLoaded = true;
        Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        Update();
    }

    /// <summary>Starts or stops the work after a change of visibility, playback or loading.</summary>
    private void Update()
    {
        if (!_isLoaded || Visibility != Visibility.Visible || _playback is null)
        {
            // Hidden: no drawing and no decoding.
            StopRendering();
            StopFallback();
            _spectrumService?.Cancel();
            _spectrum = null;
            _song = null;
            Array.Clear(_shown);
            foreach (var scale in _scales)
                if (scale is not null)
                    scale.ScaleY = MinScale;
            return;
        }

        if (IsPlaying) RefreshSpectrum();
        StartRendering();
    }

    private void RefreshSpectrum()
    {
        var song = _playback?.CurrentTrack;
        if (ReferenceEquals(song, _song) && (_spectrum is not null || song is null)) return;

        _song = song;
        _reportedPosition = null;
        _spectrum = song is not null ? _spectrumService?.Request(song) : null;
    }

    private void OnRendering(object? sender, object e)
    {
        var now = _clock.Elapsed;
        var elapsed = Math.Clamp((now - _lastTick).TotalSeconds, 0, 0.1);
        _lastTick = now;

        var playing = IsPlaying;
        if (playing) RefreshSpectrum();

        if (_spectrumService is null || _spectrum?.Status == TrackSpectrumStatus.Unavailable)
        {
            RunFallback(playing);
            return;
        }

        StopFallback();

        var hasLevels = playing && _spectrum is not null && _spectrum.TryGetLevels(EstimatePosition(now), _target);
        if (!hasLevels) Array.Clear(_target);

        var atRest = true;
        for (var i = 0; i < BarCount; i++)
        {
            var target = (double)_target[i];
            var shown = target >= _shown[i] ? target : Math.Max(target, _shown[i] - FallPerSecond * elapsed);
            _shown[i] = shown;
            _scales[i].ScaleY = MinScale + (1 - MinScale) * shown;
            if (shown > 0) atRest = false;
        }

        if (!playing && atRest) StopRendering();
    }

    /// <summary>
    ///     The player reports its position in steps; between reports the position is moved on with
    ///     the clock so the bars stay smooth.
    /// </summary>
    private TimeSpan EstimatePosition(TimeSpan now)
    {
        var reported = _playback!.CurrentPosition;
        if (reported != _reportedPosition)
        {
            _reportedPosition = reported;
            _reportedAt = now;
        }

        var ahead = now - _reportedAt;
        return reported + (ahead < MaxExtrapolation ? ahead : MaxExtrapolation);
    }

    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _lastTick = _clock.Elapsed;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void RunFallback(bool playing)
    {
        if (_fallback is null) return;
        if (playing)
        {
            if (_fallbackState == FallbackState.Stopped) _fallback.Begin();
            else if (_fallbackState == FallbackState.Paused) _fallback.Resume();
            _fallbackState = FallbackState.Running;
            return;
        }

        if (_fallbackState == FallbackState.Running)
        {
            _fallback.Pause();
            _fallbackState = FallbackState.Paused;
        }

        StopRendering();
    }

    private void StopFallback()
    {
        if (_fallback is null || _fallbackState == FallbackState.Stopped) return;
        _fallback.Stop();
        _fallbackState = FallbackState.Stopped;
    }

    private void CreateBars()
    {
        var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom };
        for (var i = 0; i < BarCount; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fill = (Brush)Application.Current.Resources["AppPrimaryColorBrush"];
        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        for (var i = 0; i < BarCount; i++)
        {
            var scale = new ScaleTransform { ScaleY = MinScale };
            _scales[i] = scale;
            var bar = new Rectangle
            {
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = fill,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 52,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1),
                RenderTransform = scale,
                Opacity = 0.85
            };
            Grid.SetColumn(bar, i);
            row.Children.Add(bar);

            // The decorative animation used when there is no spectrum to show.
            var start = 0.25 + i % 5 * 0.08;
            var duration = 0.55 + i % 7 * 0.11;
            var frames = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(frames, scale);
            Storyboard.SetTargetProperty(frames, "ScaleY");
            frames.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = start });
            frames.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(duration * 0.28), Value = 0.95 - i % 4 * 0.08 });
            frames.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(duration * 0.55), Value = 0.22 + i % 5 * 0.05 });
            frames.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(duration * 0.78), Value = 0.7 - i % 3 * 0.08 });
            frames.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(duration), Value = start });
            storyboard.Children.Add(frames);
        }

        BarsHost.Children.Add(row);
        _fallback = storyboard;
    }

    private enum FallbackState
    {
        Stopped,
        Running,
        Paused
    }
}
