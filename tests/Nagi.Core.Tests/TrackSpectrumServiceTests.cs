using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Implementations;
using NSubstitute;
using Xunit;

namespace Nagi.Core.Tests;

public class TrackSpectrumServiceTests
{
    private readonly IFFmpegService _ffmpeg = Substitute.For<IFFmpegService>();
    private readonly FakeExtractor _extractor = new();

    public TrackSpectrumServiceTests()
    {
        _ffmpeg.IsFFmpegInstalledAsync(Arg.Any<bool>()).Returns(true);
    }

    private TrackSpectrumService CreateService() =>
        new(_extractor, _ffmpeg, NullLogger<TrackSpectrumService>.Instance);

    private static Song CreateSong(string path = "C:\\music\\a.flac", long startTicks = 0, int cueTrack = 0) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Song",
        FilePath = path,
        Duration = TimeSpan.FromSeconds(3),
        StartOffsetTicks = startTicks,
        CueTrackNumber = cueTrack
    };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        condition().Should().BeTrue();
    }

    [Fact]
    public async Task Request_DecodesTheTrackInTheBackground()
    {
        using var service = CreateService();
        var spectrum = service.Request(CreateSong());

        await WaitForAsync(() => spectrum.Status == TrackSpectrumStatus.Complete);
        var levels = new float[SpectrumAnalyzer.BandCount];
        spectrum.TryGetLevels(TimeSpan.FromSeconds(1), levels).Should().BeTrue();
        levels.Max().Should().BeGreaterThan(0.5f);

        var options = _extractor.Requests.Single().Options;
        options.SampleRate.Should().Be(SpectrumAnalyzer.SampleRate);
        options.Channels.Should().Be(1);
        options.LowPriority.Should().BeTrue();
        options.Start.Should().BeNull();
    }

    [Fact]
    public async Task Request_ForTheSameSong_ReusesTheAnalysis()
    {
        using var service = CreateService();
        var song = CreateSong();

        var first = service.Request(song);
        await WaitForAsync(() => first.Status == TrackSpectrumStatus.Complete);

        service.Request(song).Should().BeSameAs(first);
        _extractor.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Request_ForAnotherSong_CancelsThePreviousAnalysis()
    {
        _extractor.HoldOpen = true;
        using var service = CreateService();

        service.Request(CreateSong("C:\\music\\a.flac"));
        await WaitForAsync(() => _extractor.Requests.Count == 1);
        service.Request(CreateSong("C:\\music\\b.flac"));

        await WaitForAsync(() => _extractor.Requests.Count == 2);
        await WaitForAsync(() => _extractor.Requests[0].Token.IsCancellationRequested);
        _extractor.Requests[1].Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Request_ForACueTrack_DecodesOnlyItsPart()
    {
        using var service = CreateService();
        var spectrum = service.Request(CreateSong(startTicks: TimeSpan.FromMinutes(4).Ticks, cueTrack: 2));

        await WaitForAsync(() => spectrum.Status == TrackSpectrumStatus.Complete);
        var options = _extractor.Requests.Single().Options;
        options.Start.Should().Be(TimeSpan.FromMinutes(4));
        options.Duration.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task WithoutFFmpeg_TheSpectrumIsUnavailable()
    {
        _ffmpeg.IsFFmpegInstalledAsync(Arg.Any<bool>()).Returns(false);
        using var service = CreateService();

        var spectrum = service.Request(CreateSong());

        await WaitForAsync(() => spectrum.Status == TrackSpectrumStatus.Unavailable);
        _extractor.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AFileFFmpegCannotRead_IsUnavailable()
    {
        _extractor.YieldNothing = true;
        using var service = CreateService();

        var spectrum = service.Request(CreateSong());

        await WaitForAsync(() => spectrum.Status == TrackSpectrumStatus.Unavailable);
    }

    [Fact]
    public async Task Cancel_DropsAnUnfinishedAnalysis_SoTheNextRequestStartsOver()
    {
        _extractor.HoldOpen = true;
        using var service = CreateService();
        var song = CreateSong();

        var first = service.Request(song);
        await WaitForAsync(() => _extractor.Requests.Count == 1);
        service.Cancel();

        await WaitForAsync(() => _extractor.Requests[0].Token.IsCancellationRequested);
        service.Request(song).Should().NotBeSameAs(first);
    }

    [Fact]
    public void TryGetLevels_BeforeTheFrameIsReady_ReturnsFalse()
    {
        var spectrum = new TrackSpectrum(2, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(1));
        spectrum.Append(new byte[] { 255, 0 });
        spectrum.Append(new byte[] { 0, 255 });
        var levels = new float[2];

        spectrum.TryGetLevels(TimeSpan.FromMilliseconds(25), levels).Should().BeTrue();
        levels[0].Should().BeApproximately(0.5f, 0.01f);
        levels[1].Should().BeApproximately(0.5f, 0.01f);
        spectrum.TryGetLevels(TimeSpan.FromMilliseconds(100), levels).Should().BeFalse();
    }

    /// <summary>Plays a 1 kHz tone for the requested length, or holds the stream open.</summary>
    private sealed class FakeExtractor : IPcmExtractor
    {
        private readonly object _lock = new();
        private readonly List<(PcmExtractionOptions Options, CancellationToken Token)> _requests = new();

        public bool HoldOpen { get; set; }

        public bool YieldNothing { get; set; }

        public IReadOnlyList<(PcmExtractionOptions Options, CancellationToken Token)> Requests
        {
            get { lock (_lock) return _requests.ToList(); }
        }

        public Task<(float[] Samples, int SampleRate, int Channels)?> ExtractAsync(string filePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<AudioChunk> ExtractStreamingAsync(string filePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<AudioChunk> ExtractStreamingAsync(string filePath,
            PcmExtractionOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            lock (_lock) _requests.Add((options, cancellationToken));
            if (HoldOpen) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (YieldNothing) yield break;

            var seconds = options.Duration?.TotalSeconds ?? 3;
            var samples = new float[(int)(options.SampleRate * seconds)];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / options.SampleRate));

            for (var offset = 0; offset < samples.Length; offset += 8192)
            {
                var length = Math.Min(8192, samples.Length - offset);
                var chunk = new float[length];
                Array.Copy(samples, offset, chunk, 0, length);
                yield return new AudioChunk(chunk, length, options.SampleRate, options.Channels);
            }
        }
    }
}
