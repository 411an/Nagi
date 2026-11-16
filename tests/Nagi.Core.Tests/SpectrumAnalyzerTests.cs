using FluentAssertions;
using Nagi.Core.Services.Implementations;
using Xunit;

namespace Nagi.Core.Tests;

public class SpectrumAnalyzerTests
{
    private static List<byte[]> Analyze(float[] samples, int chunkSize = 4096)
    {
        var frames = new List<byte[]>();
        var analyzer = new SpectrumAnalyzer();
        for (var offset = 0; offset < samples.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, samples.Length - offset);
            analyzer.Process(samples.AsSpan(offset, length), f => frames.Add(f.ToArray()));
        }

        analyzer.Flush(f => frames.Add(f.ToArray()));
        return frames;
    }

    private static float[] Sine(double frequency, double amplitude, double seconds)
    {
        var samples = new float[(int)(SpectrumAnalyzer.SampleRate * seconds)];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / SpectrumAnalyzer.SampleRate));
        return samples;
    }

    private static int BandOf(double frequency)
    {
        for (var band = 0; band < SpectrumAnalyzer.BandCount; band++)
        {
            var (low, high) = SpectrumAnalyzer.GetBandRange(band);
            if (frequency >= low && frequency < high) return band;
        }

        throw new InvalidOperationException($"{frequency} Hz is outside the bands.");
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(1000.0)]
    [InlineData(8000.0)]
    public void ASine_LightsUpItsOwnBand(double frequency)
    {
        // -14 dB keeps the peak below the top of the scale, so neighbouring bars do not tie at 255.
        var frames = Analyze(Sine(frequency, 0.2, 2));
        var middle = frames[frames.Count / 2];
        var band = BandOf(frequency);

        middle[band].Should().BeInRange(200, 254, "a -14 dB sine should nearly fill its bar");
        Array.IndexOf(middle, middle.Max()).Should().Be(band);
        for (var other = 0; other < middle.Length; other++)
            if (Math.Abs(other - band) > 2)
                middle[other].Should().BeLessThan(60, $"band {other} is far from {frequency} Hz");
    }

    [Fact]
    public void Silence_GivesEmptyBars()
    {
        var frames = Analyze(new float[SpectrumAnalyzer.SampleRate]);
        frames.Should().NotBeEmpty();
        frames.Should().OnlyContain(f => f.All(level => level == 0));
    }

    [Fact]
    public void FrameCount_FollowsTheTrackLength()
    {
        const double seconds = 10;
        var frames = Analyze(Sine(440, 0.3, seconds), chunkSize: 777);
        var expected = seconds / SpectrumAnalyzer.FrameDuration.TotalSeconds;
        frames.Count.Should().BeCloseTo((int)Math.Round(expected), 2);
    }

    [Fact]
    public void Frames_LineUpWithTime()
    {
        // One second of silence, then a tone: the bar rises at the one-second frame.
        var samples = new float[SpectrumAnalyzer.SampleRate * 2];
        Sine(1000, 0.5, 1).CopyTo(samples, SpectrumAnalyzer.SampleRate);
        var frames = Analyze(samples);
        var band = BandOf(1000);
        var onset = (int)Math.Round(1.0 / SpectrumAnalyzer.FrameDuration.TotalSeconds);

        frames[onset - 2][band].Should().Be(0);
        frames[onset + 2][band].Should().BeGreaterThan(200);
    }

    [Fact]
    public void NoInput_GivesNoFrames()
    {
        Analyze(Array.Empty<float>()).Should().BeEmpty();
    }
}
