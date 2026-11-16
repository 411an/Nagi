using System.Globalization;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Implementations;
using Nagi.Core.Tests.Utils;
using Xunit;

namespace Nagi.Core.Tests;

public class ReplayGainServiceTests
{
    [Fact]
    public async Task ScanLibraryAsync_DuringAPass_ReturnsAtOnceAndThePassRunsOneMoreRound()
    {
        using var database = new DbContextFactoryTestHelper();
        await using (var context = database.ContextFactory.CreateDbContext())
        {
            context.Songs.Add(new Song
            {
                FilePath = @"C:\Music\one.mp3",
                DirectoryPath = @"C:\Music",
                Folder = new Folder { Path = @"C:\Music" }
            });
            await context.SaveChangesAsync();
        }

        var extractor = new GatedPcmExtractor();
        var service = new ReplayGainService(database.ContextFactory, extractor,
            NullLogger<ReplayGainService>.Instance);

        var firstPass = service.ScanLibraryAsync();
        await extractor.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A second caller (for example Settings turning normalization on) must not start its own pass.
        await service.ScanLibraryAsync().WaitAsync(TimeSpan.FromSeconds(5));
        extractor.Calls.Should().Be(1);

        // The song still has no gain because extraction yields nothing, so the extra round analyzes it again.
        extractor.ReleaseFirst.SetResult();
        await firstPass.WaitAsync(TimeSpan.FromSeconds(5));
        extractor.Calls.Should().Be(2);
    }

    [Fact]
    public void ReplayGainTags_UseADotWhateverTheCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            ReplayGainService.FormatGain(-3.214).Should().Be("-3.21 dB");
            ReplayGainService.FormatPeak(0.9876543).Should().Be("0.987654");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    ///     Holds the first extraction open until released and yields no audio, so no gain is ever written.
    /// </summary>
    private sealed class GatedPcmExtractor : IPcmExtractor
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(float[] Samples, int SampleRate, int Channels)?> ExtractAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<(float[] Samples, int SampleRate, int Channels)?>(null);
        }

        public async IAsyncEnumerable<AudioChunk> ExtractStreamingAsync(string filePath,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstStarted.SetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            yield break;
        }

        public IAsyncEnumerable<AudioChunk> ExtractStreamingAsync(string filePath, PcmExtractionOptions options,
            CancellationToken cancellationToken = default)
        {
            return ExtractStreamingAsync(filePath, cancellationToken);
        }
    }
}
