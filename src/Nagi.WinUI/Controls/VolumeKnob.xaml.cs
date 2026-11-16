using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Nagi.WinUI.Controls;

public sealed partial class VolumeKnob : UserControl
{
    public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register(
        nameof(Volume),
        typeof(double),
        typeof(VolumeKnob),
        new PropertyMetadata(50d, OnVolumeChanged));

    private bool _isDragging;
    private bool _ticksBuilt;

    public VolumeKnob()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VolumeKnob knob) knob.UpdateIndicator();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildTicks();
        UpdateIndicator();
    }

    private void BuildTicks()
    {
        if (_ticksBuilt) return;
        _ticksBuilt = true;

        var fill = (Brush)Application.Current.Resources["AppPrimaryColorBrush"];
        const double cx = 60;
        const double cy = 60;
        const int tickCount = 24;
        for (var i = 0; i <= tickCount; i++)
        {
            var sweep = i / (double)tickCount * 270.0;
            var angle = (-135 + sweep) * Math.PI / 180.0;
            var inner = i % 4 == 0 ? 50 : 53;
            var outer = 58;
            var tick = new Line
            {
                X1 = cx + Math.Cos(angle) * inner,
                Y1 = cy + Math.Sin(angle) * inner,
                X2 = cx + Math.Cos(angle) * outer,
                Y2 = cy + Math.Sin(angle) * outer,
                Stroke = fill,
                StrokeThickness = i % 4 == 0 ? 2 : 1,
                Opacity = i % 4 == 0 ? 0.9 : 0.35
            };
            TickCanvas.Children.Add(tick);
        }
    }

    private void UpdateIndicator()
    {
        var clamped = Math.Clamp(Volume, 0, 100);
        DotRotate.Angle = -135 + clamped / 100.0 * 270.0;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = true;
        CapturePointer(e.Pointer);
        ApplyPoint(e.GetCurrentPoint(this).Position);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        ApplyPoint(e.GetCurrentPoint(this).Position);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta > 0 ? 5 : -5;
        Volume = Math.Clamp(Volume + delta, 0, 100);
        e.Handled = true;
    }

    private void ApplyPoint(Point point)
    {
        var cx = ActualWidth / 2.0;
        var cy = ActualHeight / 2.0;
        var angle = Math.Atan2(point.Y - cy, point.X - cx) * 180.0 / Math.PI;
        var sweep = angle + 135;
        if (sweep < 0) sweep += 360;
        if (sweep > 270) sweep = point.X < cx ? 0 : 270;
        Volume = Math.Clamp(sweep / 270.0 * 100.0, 0, 100);
    }
}
