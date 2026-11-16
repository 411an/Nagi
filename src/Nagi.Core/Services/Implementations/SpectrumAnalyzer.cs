namespace Nagi.Core.Services.Implementations;

/// <summary>
///     Turns mono PCM into frames of 28 bar levels for the visualizer, about 21 frames a second.
///     Each frame is a 2048-point FFT of a Hann-windowed slice centred on the frame's time, with
///     the power summed into log-spaced bands from 40 Hz to 11 kHz. Summing (not averaging) the
///     power keeps pink noise, and so most music, roughly level across the bars.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int SampleRate = 22050;
    public const int BandCount = 28;
    public const int FftSize = 2048;
    public const int HopSize = 1024;

    private const double MinFrequency = 40.0;
    private const double MaxFrequency = 11000.0;

    // Band power in dB relative to a full-scale sine, mapped onto the bar height. Music at
    // ordinary loudness puts most bands between -45 and -15 dB.
    private const double FloorDb = -62.0;
    private const double CeilingDb = -10.0;

    public static readonly TimeSpan FrameDuration = TimeSpan.FromSeconds((double)HopSize / SampleRate);

    private static readonly double[] Window = CreateHannWindow();
    private static readonly (int Start, int End)[] BandBins = CreateBandBins();
    private static readonly int[] BitReversed = CreateBitReversal();
    private static readonly double[] Cos = new double[FftSize / 2];
    private static readonly double[] Sin = new double[FftSize / 2];

    // Power of a full-scale sine in its strongest bin after the Hann window: (N / 4)^2.
    private static readonly double ReferencePower = Math.Pow(FftSize / 4.0, 2);

    private readonly float[] _buffer = new float[FftSize];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly byte[] _frame = new byte[BandCount];
    private int _filled;
    private bool _hasInput;

    static SpectrumAnalyzer()
    {
        for (var i = 0; i < FftSize / 2; i++)
        {
            Cos[i] = Math.Cos(2 * Math.PI * i / FftSize);
            Sin[i] = -Math.Sin(2 * Math.PI * i / FftSize);
        }
    }

    public SpectrumAnalyzer()
    {
        // Half a window of silence first, so frame k is centred on k * FrameDuration.
        _filled = FftSize / 2;
    }

    /// <summary>
    ///     Feeds samples and hands every completed frame to <paramref name="onFrame" />. The span
    ///     passed to the callback is reused for the next frame.
    /// </summary>
    public void Process(ReadOnlySpan<float> samples, Action<ReadOnlySpan<byte>> onFrame)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(FftSize - _filled, samples.Length);
            samples[..take].CopyTo(_buffer.AsSpan(_filled));
            _filled += take;
            samples = samples[take..];
            _hasInput |= take > 0;

            if (_filled < FftSize) continue;
            EmitFrame(onFrame);
            _buffer.AsSpan(HopSize).CopyTo(_buffer);
            _filled = FftSize - HopSize;
        }
    }

    /// <summary>
    ///     Emits the frames still waiting for samples, padding the end of the track with silence.
    /// </summary>
    public void Flush(Action<ReadOnlySpan<byte>> onFrame)
    {
        if (!_hasInput) return;
        while (_filled > FftSize / 2)
        {
            _buffer.AsSpan(_filled).Clear();
            EmitFrame(onFrame);
            _buffer.AsSpan(HopSize).CopyTo(_buffer);
            _filled -= HopSize;
        }
    }

    private void EmitFrame(Action<ReadOnlySpan<byte>> onFrame)
    {
        for (var i = 0; i < FftSize; i++)
        {
            _re[BitReversed[i]] = _buffer[i] * Window[i];
            _im[i] = 0;
        }

        Transform();

        for (var band = 0; band < BandCount; band++)
        {
            var (start, end) = BandBins[band];
            var power = 0.0;
            for (var bin = start; bin < end; bin++) power += _re[bin] * _re[bin] + _im[bin] * _im[bin];

            var db = 10 * Math.Log10(power / ReferencePower + 1e-12);
            var level = Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0, 1);
            _frame[band] = (byte)Math.Round(level * 255);
        }

        onFrame(_frame);
    }

    // In-place iterative radix-2 FFT; the input is already in bit-reversed order.
    private void Transform()
    {
        for (var size = 2; size <= FftSize; size <<= 1)
        {
            var half = size / 2;
            var step = FftSize / size;
            for (var start = 0; start < FftSize; start += size)
            for (var k = 0; k < half; k++)
            {
                var a = start + k;
                var b = a + half;
                var wr = Cos[k * step];
                var wi = Sin[k * step];
                var tr = wr * _re[b] - wi * _im[b];
                var ti = wr * _im[b] + wi * _re[b];
                _re[b] = _re[a] - tr;
                _im[b] = _im[a] - ti;
                _re[a] += tr;
                _im[a] += ti;
            }
        }
    }

    private static double[] CreateHannWindow()
    {
        var window = new double[FftSize];
        for (var i = 0; i < FftSize; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize);
        return window;
    }

    private static int[] CreateBitReversal()
    {
        var bits = (int)Math.Log2(FftSize);
        var result = new int[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            var reversed = 0;
            for (var b = 0; b < bits; b++)
                if ((i & (1 << b)) != 0)
                    reversed |= 1 << (bits - 1 - b);
            result[i] = reversed;
        }

        return result;
    }

    private static (int Start, int End)[] CreateBandBins()
    {
        var binWidth = (double)SampleRate / FftSize;
        var bins = new (int Start, int End)[BandCount];
        var previousEnd = 1; // skip the DC bin
        for (var band = 0; band < BandCount; band++)
        {
            var low = MinFrequency * Math.Pow(MaxFrequency / MinFrequency, (double)band / BandCount);
            var high = MinFrequency * Math.Pow(MaxFrequency / MinFrequency, (double)(band + 1) / BandCount);
            var start = Math.Max((int)Math.Round(low / binWidth), previousEnd);
            var end = Math.Max((int)Math.Round(high / binWidth), start + 1);
            end = Math.Min(end, FftSize / 2);
            bins[band] = (start, end);
            previousEnd = end;
        }

        return bins;
    }

    /// <summary>
    ///     Frequency range of a band in Hz, for tests and diagnostics: from the lower edge of its
    ///     first bin to the lower edge of the next band's first bin.
    /// </summary>
    public static (double Low, double High) GetBandRange(int band)
    {
        var binWidth = (double)SampleRate / FftSize;
        var (start, end) = BandBins[band];
        return ((start - 0.5) * binWidth, (end - 0.5) * binWidth);
    }
}
