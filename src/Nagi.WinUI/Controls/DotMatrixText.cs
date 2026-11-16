using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Nagi.WinUI.Controls;

/// <summary>
///     Renders a short string as a 5x7 LED dot matrix. Unlit dots stay faintly visible in
///     <see cref="OffBrush" />, like a hardware display. Digits, ':', '.', '-', space and the
///     <see cref="PlayGlyph" /> / <see cref="PauseGlyph" /> marks are defined; anything else renders blank.
/// </summary>
public sealed class DotMatrixText : UserControl
{
    public const char PlayGlyph = '▶';
    public const char PauseGlyph = '‖';

    private const int GlyphColumns = 5;
    private const int GlyphRows = 7;

    // One byte per row; the highest of the five bits is the leftmost dot.
    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['0'] = new byte[] { 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110 },
        ['1'] = new byte[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
        ['2'] = new byte[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111 },
        ['3'] = new byte[] { 0b11111, 0b00010, 0b00100, 0b00010, 0b00001, 0b10001, 0b01110 },
        ['4'] = new byte[] { 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010 },
        ['5'] = new byte[] { 0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110 },
        ['6'] = new byte[] { 0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110 },
        ['7'] = new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000 },
        ['8'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110 },
        ['9'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100 },
        [':'] = new byte[] { 0b00000, 0b01100, 0b01100, 0b00000, 0b01100, 0b01100, 0b00000 },
        ['.'] = new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100 },
        ['-'] = new byte[] { 0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000 },
        [PlayGlyph] = new byte[] { 0b01000, 0b01100, 0b01110, 0b01111, 0b01110, 0b01100, 0b01000 },
        [PauseGlyph] = new byte[] { 0b00000, 0b11011, 0b11011, 0b11011, 0b11011, 0b11011, 0b00000 }
    };

    private static readonly byte[] BlankGlyph = new byte[GlyphRows];

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DotMatrixText),
        new PropertyMetadata(string.Empty, (d, _) => ((DotMatrixText)d).Render(false)));

    public static readonly DependencyProperty DotSizeProperty = DependencyProperty.Register(
        nameof(DotSize), typeof(double), typeof(DotMatrixText),
        new PropertyMetadata(3.0, (d, _) => ((DotMatrixText)d).Rebuild()));

    public static readonly DependencyProperty DotSpacingProperty = DependencyProperty.Register(
        nameof(DotSpacing), typeof(double), typeof(DotMatrixText),
        new PropertyMetadata(1.0, (d, _) => ((DotMatrixText)d).Rebuild()));

    public static readonly DependencyProperty OnBrushProperty = DependencyProperty.Register(
        nameof(OnBrush), typeof(Brush), typeof(DotMatrixText),
        new PropertyMetadata(null, (d, _) => ((DotMatrixText)d).Render(true)));

    public static readonly DependencyProperty OffBrushProperty = DependencyProperty.Register(
        nameof(OffBrush), typeof(Brush), typeof(DotMatrixText),
        new PropertyMetadata(null, (d, _) => ((DotMatrixText)d).Render(true)));

    private readonly Canvas _canvas = new();
    private readonly List<Rectangle[]> _cells = new();
    private string _shownText = string.Empty;

    public DotMatrixText()
    {
        Content = _canvas;
        ResizeCells(0);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public double DotSpacing
    {
        get => (double)GetValue(DotSpacingProperty);
        set => SetValue(DotSpacingProperty, value);
    }

    public Brush? OnBrush
    {
        get => (Brush?)GetValue(OnBrushProperty);
        set => SetValue(OnBrushProperty, value);
    }

    public Brush? OffBrush
    {
        get => (Brush?)GetValue(OffBrushProperty);
        set => SetValue(OffBrushProperty, value);
    }

    private void Rebuild()
    {
        _canvas.Children.Clear();
        _cells.Clear();
        ResizeCells(0);
        Render(true);
    }

    private void Render(bool repaintAll)
    {
        var text = Text ?? string.Empty;
        if (text.Length != _cells.Count)
        {
            ResizeCells(text.Length);
            repaintAll = true;
        }

        for (var i = 0; i < text.Length; i++)
        {
            // Only characters that changed need new fills; the clock changes one digit per second.
            if (!repaintAll && i < _shownText.Length && _shownText[i] == text[i]) continue;

            var glyph = Glyphs.TryGetValue(text[i], out var defined) ? defined : BlankGlyph;
            var dots = _cells[i];
            for (var row = 0; row < GlyphRows; row++)
            for (var col = 0; col < GlyphColumns; col++)
            {
                var lit = ((glyph[row] >> (GlyphColumns - 1 - col)) & 1) == 1;
                dots[row * GlyphColumns + col].Fill = lit ? OnBrush : OffBrush;
            }
        }

        _shownText = text;
    }

    private void ResizeCells(int count)
    {
        var pitch = DotSize + DotSpacing;
        // A blank dot column separates neighbouring characters.
        var cellWidth = (GlyphColumns + 1) * pitch;
        var radius = DotSize / 3;

        while (_cells.Count > count)
        {
            foreach (var dot in _cells[^1]) _canvas.Children.Remove(dot);
            _cells.RemoveAt(_cells.Count - 1);
        }

        while (_cells.Count < count)
        {
            var index = _cells.Count;
            var dots = new Rectangle[GlyphColumns * GlyphRows];
            for (var row = 0; row < GlyphRows; row++)
            for (var col = 0; col < GlyphColumns; col++)
            {
                var dot = new Rectangle { Width = DotSize, Height = DotSize, RadiusX = radius, RadiusY = radius };
                Canvas.SetLeft(dot, index * cellWidth + col * pitch);
                Canvas.SetTop(dot, row * pitch);
                _canvas.Children.Add(dot);
                dots[row * GlyphColumns + col] = dot;
            }

            _cells.Add(dots);
        }

        _canvas.Width = count == 0 ? 0 : (count - 1) * cellWidth + (GlyphColumns - 1) * pitch + DotSize;
        _canvas.Height = (GlyphRows - 1) * pitch + DotSize;
    }
}
