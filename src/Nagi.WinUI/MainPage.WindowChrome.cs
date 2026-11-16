using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nagi.Core.Services.Abstractions;
using Nagi.WinUI.Controls;
using WinRT.Interop;

namespace Nagi.WinUI;

/// <summary>
///     The title strip that replaces the Windows title bar, and moving the window by any empty spot.
/// </summary>
public sealed partial class MainPage : ICustomTitleBarProvider
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int SmCxDrag = 68;
    private const int SmCyDrag = 69;

    // Where the left button went down on an empty spot; the window moves once the mouse leaves
    // the system drag threshold, so a plain click still reaches whatever was clicked.
    private Windows.Foundation.Point? _dragStart;

    public string AppVersion { get; } = App.Services!.GetRequiredService<IAppInfoService>().GetAppVersion();

    public UIElement GetAppTitleBarElement() => AppTitleBar;

    public RowDefinition GetAppTitleBarRowElement() => AppTitleBarRow;

    /// <summary>"Artist — Title" of the loaded track for the title strip, or nothing.</summary>
    public string FormatNowPlaying(string? artist, string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title == Nagi.WinUI.Resources.Strings.Status_NoTrackPlaying)
            return string.Empty;
        return string.IsNullOrWhiteSpace(artist) ? title : $"{artist} — {title}";
    }

    /// <summary>Keeps the strip's text clear of the minimize, maximize and close buttons.</summary>
    public void UpdateTitleBarInset()
    {
        var titleBar = App.RootWindow?.AppWindow?.TitleBar;
        if (titleBar is null) return;
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        if (titleBar.RightInset > 0) CaptionButtonsColumn.Width = new GridLength(titleBar.RightInset / scale);
        AppTitleBar.Padding = new Thickness(12 + titleBar.LeftInset / scale, 0, 0, 0);
    }

    private void InitializeWindowChrome()
    {
        ShellRoot.AddHandler(PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed), false);
        ShellRoot.AddHandler(PointerMovedEvent, new PointerEventHandler(OnShellPointerMoved), false);
        ShellRoot.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnShellPointerEnded), true);
        ShellRoot.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnShellPointerEnded), true);
        ShellRoot.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnShellPointerEnded), true);
        ShellRoot.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler(OnShellDoubleTapped), false);
        Loaded += OnChromeLoaded;
        Unloaded += OnChromeUnloaded;
    }

    private void OnChromeLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBarInset();
        if (XamlRoot is not null) XamlRoot.Changed += OnXamlRootChanged;
    }

    private void OnChromeUnloaded(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not null) XamlRoot.Changed -= OnXamlRootChanged;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateTitleBarInset();

    private void SetTitleBarActive(bool isActive) => AppTitleBar.Opacity = isActive ? 1.0 : 0.6;

    private void OnShellPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragStart = null;
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        var point = e.GetCurrentPoint(ShellRoot);
        if (!point.Properties.IsLeftButtonPressed || IsInteractive(e.OriginalSource as DependencyObject)) return;
        _dragStart = point.Position;
    }

    private void OnShellPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var point = e.GetCurrentPoint(ShellRoot);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _dragStart = null;
            return;
        }

        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var dx = Math.Abs(point.Position.X - start.X) * scale;
        var dy = Math.Abs(point.Position.Y - start.Y) * scale;
        if (dx < GetSystemMetrics(SmCxDrag) && dy < GetSystemMetrics(SmCyDrag)) return;

        _dragStart = null;
        if (App.RootWindow is null) return;

        // Hand the drag to Windows as if the title bar had been grabbed: the window follows the
        // mouse, snaps to screen edges, and a maximized window is restored first.
        var hwnd = WindowNative.GetWindowHandle(App.RootWindow);
        GetCursorPos(out var cursor);
        ReleaseCapture();
        SendMessage(hwnd, WmNcLButtonDown, HtCaption, (cursor.Y << 16) | (cursor.X & 0xFFFF));
    }

    private void OnShellPointerEnded(object sender, PointerRoutedEventArgs e) => _dragStart = null;

    private void OnShellDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.PointerDeviceType != PointerDeviceType.Mouse) return;
        if (e.OriginalSource is not (Panel or Border) || IsInteractive(e.OriginalSource as DependencyObject)) return;
        if (App.RootWindow?.AppWindow?.Presenter is not OverlappedPresenter presenter) return;

        if (presenter.State == OverlappedPresenterState.Maximized)
            presenter.Restore();
        else
            presenter.Maximize();
        e.Handled = true;
    }

    /// <summary>True when the element, or one it sits in, reacts to the mouse itself.</summary>
    private bool IsInteractive(DependencyObject? source)
    {
        for (var element = source; element is not null && !ReferenceEquals(element, ShellRoot);
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase or RangeBase or Thumb or TextBox or PasswordBox or RichEditBox
                or AutoSuggestBox or ToggleSwitch or ComboBox or SelectorItem or TreeViewItem or VolumeKnob)
                return true;
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
}
