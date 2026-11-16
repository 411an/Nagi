using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nagi.WinUI.Resources;
using Nagi.WinUI.ViewModels;

namespace Nagi.WinUI.Controls;

/// <summary>
///     LED-style status panel: lossless lamp, queue position, play state, elapsed or remaining time and bitrate.
/// </summary>
public sealed partial class PlayerStatusStrip : UserControl
{
    private static readonly HashSet<string> StatusProperties = new()
    {
        nameof(PlayerViewModel.IsLossless),
        nameof(PlayerViewModel.QueuePosition),
        nameof(PlayerViewModel.QueueTotal),
        nameof(PlayerViewModel.IsPlaying),
        nameof(PlayerViewModel.ShowRemaining),
        nameof(PlayerViewModel.StatusTimeText),
        nameof(PlayerViewModel.BitrateKbps)
    };

    private PlayerViewModel? _viewModel;

    public PlayerStatusStrip()
    {
        InitializeComponent();
        Refresh();
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
                value.PropertyChanged += OnViewModelPropertyChanged;
            Refresh();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The player raises position updates several times a second; only status fields repaint the panel.
        if (string.IsNullOrEmpty(e.PropertyName) || StatusProperties.Contains(e.PropertyName)) Refresh();
    }

    private void Refresh()
    {
        var vm = _viewModel;
        var isLossless = vm?.IsLossless == true;
        var position = vm?.QueuePosition ?? 0;
        var total = vm?.QueueTotal ?? 0;
        var kbps = vm?.BitrateKbps ?? 0;
        var clock = ToClock(vm?.StatusTimeText);

        LosslessLamp.Opacity = isLossless ? 1.0 : 0.2;
        TrackDigits.Text = position.ToString("00", CultureInfo.InvariantCulture);
        TotalDigits.Text = total.ToString("00", CultureInfo.InvariantCulture);
        StateGlyph.Text = (vm?.IsPlaying == true ? DotMatrixText.PlayGlyph : DotMatrixText.PauseGlyph).ToString();
        TimeLabel.Text = vm?.ShowRemaining == true ? Strings.Shell_Remaining : Strings.Shell_Elapsed;
        TimeDigits.Text = clock;
        BitrateDigits.Text = kbps > 0 ? kbps.ToString(CultureInfo.InvariantCulture).PadLeft(4) : "----";

        // The dots are shapes, so give screen readers the same information as text.
        var lamp = isLossless ? Strings.Shell_Lossless + ", " : string.Empty;
        AutomationProperties.SetName(LcdPanel,
            $"{lamp}{Strings.Shell_Track} {position}, {Strings.Shell_Total} {total}, {TimeLabel.Text} {clock}, {Strings.Shell_Kbps} {kbps}");
    }

    // The view model drops the hour field below one hour; the display keeps hh:mm:ss so digits never shift.
    private static string ToClock(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "00:00:00";
        return text.Length == 5 ? "00:" + text : text;
    }

    private void TimeGroup_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.ShowRemaining = !_viewModel.ShowRemaining;
        e.Handled = true;
    }
}
