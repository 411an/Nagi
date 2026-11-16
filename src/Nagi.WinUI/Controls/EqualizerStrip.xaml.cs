using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Nagi.WinUI.ViewModels;

namespace Nagi.WinUI.Controls;

public sealed partial class EqualizerStrip : UserControl
{
    private SettingsViewModel? _settings;
    private bool _suppressPreamp;

    public EqualizerStrip()
    {
        InitializeComponent();
    }

    public SettingsViewModel? Settings
    {
        get => _settings;
        set
        {
            if (_settings != null)
                _settings.PropertyChanged -= OnSettingsPropertyChanged;
            _settings = value;
            BandRepeater.ItemsSource = value?.EqualizerBands;
            if (value != null)
            {
                _suppressPreamp = true;
                PreampSlider.Value = value.EqualizerPreamp;
                _suppressPreamp = false;
                value.PropertyChanged += OnSettingsPropertyChanged;
            }
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.EqualizerPreamp) || _settings is null) return;
        _suppressPreamp = true;
        PreampSlider.Value = _settings.EqualizerPreamp;
        _suppressPreamp = false;
    }

    private void PreampSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressPreamp || _settings is null) return;
        _settings.EqualizerPreamp = (float)e.NewValue;
    }
}
