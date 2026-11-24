using FluentAssertions;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Data;
using Nagi.Core.Services.Implementations;
using NSubstitute;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class MusicPlaybackPreparationTests
{
    private readonly IAudioPlayer _audio = Substitute.For<IAudioPlayer>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IMetadataService _metadata = Substitute.For<IMetadataService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ILogger<MusicPlaybackService> _logger = Substitute.For<ILogger<MusicPlaybackService>>();

    public MusicPlaybackPreparationTests()
    {
        _settings.GetInitialVolumeAsync().Returns(.5);
        _settings.GetInitialMuteStateAsync().Returns(false);
        _settings.GetInitialShuffleStateAsync().Returns(false);
        _settings.GetInitialRepeatModeAsync().Returns(RepeatMode.Off);
        _settings.GetRestorePlaybackStateEnabledAsync().Returns(false);
        _settings.GetPlaybackStateAsync().Returns((PlaybackState?)null);
        _settings.GetEqualizerSettingsAsync().Returns((EqualizerSettings?)null);
        _audio.GetEqualizerBands().Returns(new List<(uint, float)> { (0, 60f) });
        _audio.CurrentPosition.Returns(TimeSpan.Zero);
        _audio.Duration.Returns(TimeSpan.Zero);
    }

    [Fact]
    public async Task PreparedSuccessor_IsAdoptedWithoutReloadingOrReplaying()
    {
        var (a, b, c) = Songs();
        var gateB = Gate<Song?>();
        ConfigureSongs(a, b, c, gateB.Task);
        var preparation = CapturePreparation();
        using var service = Create();

        await service.PlayAsync([a.Id, b.Id, c.Id]);
        gateB.SetResult(b);
        await Eventually(() => preparation.Count == 1);
        var loadCount = Received.LoadCount(_audio);
        var playCount = Received.PlayCount(_audio);

        _audio.PreparedTrackStarted += Raise.Event<Action<long>>(preparation.First().Id);
        await Eventually(() => service.CurrentTrack?.Id == b.Id);

        service.CurrentQueueIndex.Should().Be(1);
        Received.LoadCount(_audio).Should().Be(loadCount);
        Received.PlayCount(_audio).Should().Be(playCount);
        await Eventually(() => preparation.Count == 2);
    }

    [Fact]
    public async Task ManualSelection_CancelsObsoletePreparationAndDoesNotLateLoadB()
    {
        var (a, b, c) = Songs();
        var gateB = Gate<Song?>();
        ConfigureSongs(a, b, c, gateB.Task);
        using var service = Create();
        await service.PlayAsync([a.Id, b.Id, c.Id]);

        var manual = service.PlayQueueItemAsync(2);
        await manual;
        service.CurrentTrack!.Id.Should().Be(c.Id);
        gateB.SetResult(b);
        await service.FlushPendingFinalizationAsync();

        service.CurrentTrack!.Id.Should().Be(c.Id);
        await _audio.DidNotReceive().LoadAsync(Arg.Is<Song>(s => s.Id == b.Id));
        await _audio.Received().CancelPreparedNextAsync();
    }

    [Fact]
    public async Task StalePreparedEvent_IsIgnoredAfterManualSelection()
    {
        var (a, b, c) = Songs();
        ConfigureSongs(a, b, c);
        var preparation = CapturePreparation();
        using var service = Create();
        await service.PlayAsync([a.Id, b.Id, c.Id]);
        await Eventually(() => preparation.Count == 1);
        var staleId = preparation.First().Id;

        await service.PlayQueueItemAsync(2);
        _audio.PreparedTrackStarted += Raise.Event<Action<long>>(staleId);
        await service.FlushPendingFinalizationAsync();

        service.CurrentTrack!.Id.Should().Be(c.Id);
        await _library.DidNotReceive().StartListenSessionAsync(b.Id, Arg.Any<PlaybackContext>());
        await _audio.DidNotReceive().LoadAsync(Arg.Is<Song>(s => s.Id == b.Id));
    }

    [Fact]
    public async Task ManualNext_InvalidatesPreparedB_AndLeavesBCurrent()
    {
        var (a, b, c) = Songs();
        ConfigureSongs(a, b, c);
        var preparation = CapturePreparation();
        using var service = Create();
        await service.PlayAsync([a.Id, b.Id, c.Id]);
        await Eventually(() => preparation.Count == 1);

        await service.NextAsync();
        service.CurrentTrack!.Id.Should().Be(b.Id);
        _audio.PreparedTrackStarted += Raise.Event<Action<long>>(preparation.First().Id);
        await service.FlushPendingFinalizationAsync();

        service.CurrentTrack!.Id.Should().Be(b.Id);
        await _audio.DidNotReceive().LoadAsync(Arg.Is<Song>(s => s.Id == c.Id));
    }

    [Fact]
    public async Task PendingStart_DoesNotBlockPlayAndIsFinalizedAfterSkip()
    {
        var (a, b, _) = Songs();
        ConfigureSongs(a, b, null);
        var start = Gate<long?>();
        _library.StartListenSessionAsync(a.Id, Arg.Any<PlaybackContext>()).Returns(start.Task);
        _library.StartListenSessionAsync(b.Id, Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(2));
        using var service = Create();

        await service.PlayAsync([a.Id, b.Id]);
        await service.PlayQueueItemAsync(1);
        start.SetResult(1);
        await service.FlushPendingFinalizationAsync();
        await _library.Received(1).FinalizeListenSessionAsync(1, Arg.Any<TimeSpan>(), PlaybackEndReason.Skipped);
    }

    [Fact]
    public async Task FinalizeFailure_DoesNotBlockPreparedHandoff()
    {
        var (a, b, c) = Songs();
        ConfigureSongs(a, b, c);
        _library.FinalizeListenSessionAsync(1, Arg.Any<TimeSpan>(), Arg.Any<PlaybackEndReason>())
            .Returns(Task.FromException(new InvalidOperationException("end")));
        _library.StartListenSessionAsync(a.Id, Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(1));
        _library.StartListenSessionAsync(b.Id, Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(2));
        var preparation = CapturePreparation();
        using var service = Create();
        await service.PlayAsync([a.Id, b.Id, c.Id]);
        await Eventually(() => preparation.Count == 1);
        _audio.PreparedTrackStarted += Raise.Event<Action<long>>(preparation.First().Id);
        await Eventually(() => service.CurrentTrack?.Id == b.Id);
        service.CurrentTrack!.Id.Should().Be(b.Id);
        await service.FlushPendingFinalizationAsync();
    }

    [Fact]
    public async Task CueSiblingsWithSamePath_AreDistinctPreparedTracks()
    {
        var a = new Song { Id = Guid.NewGuid(), FilePath = "cue.flac", CueTrackNumber = 1, Duration = TimeSpan.FromSeconds(10) };
        var b = new Song { Id = Guid.NewGuid(), FilePath = "cue.flac", CueTrackNumber = 2, StartOffsetTicks = TimeSpan.FromSeconds(10).Ticks, Duration = TimeSpan.FromSeconds(20) };
        ConfigureSongs(a, b, null);
        var preparation = CapturePreparation();
        using var service = Create();
        await service.PlayAsync([a.Id, b.Id]);
        await Eventually(() => preparation.Count == 1);

        _audio.PreparedTrackStarted += Raise.Event<Action<long>>(preparation.First().Id);
        await Eventually(() => service.CurrentTrack?.Id == b.Id);
        service.CurrentQueueIndex.Should().Be(1);
        service.CurrentTrack!.CueTrackNumber.Should().Be(2);
        service.Duration.Should().Be(b.Duration);
    }

    [Fact]
    public async Task RepeatOne_PreparesCurrentTrackAsSuccessor()
    {
        var (a, b, _) = Songs();
        ConfigureSongs(a, b, null);
        _settings.GetInitialRepeatModeAsync().Returns(RepeatMode.RepeatOne);
        var preparation = CapturePreparation();
        using var service = Create();
        await service.InitializeAsync(false);
        await service.PlayAsync([a.Id, b.Id]);
        await Eventually(() => preparation.Count == 1);
        preparation.First().Song.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task RepeatAll_PreparesFirstTrackAfterLast()
    {
        var (a, b, _) = Songs();
        ConfigureSongs(a, b, null);
        _settings.GetInitialRepeatModeAsync().Returns(RepeatMode.RepeatAll);
        var preparation = CapturePreparation();
        using var service = Create();
        await service.InitializeAsync(false);
        await service.PlayAsync([a.Id, b.Id]);
        await service.PlayQueueItemAsync(1);
        await Eventually(() => preparation.Any(x => x.Song.Id == a.Id));
    }

    [Fact]
    public async Task PendingStart_StopEventuallyFinalizesSession()
    {
        var (a, _, _) = Songs();
        ConfigureSongs(a, a, null);
        var start = Gate<long?>();
        _library.StartListenSessionAsync(a.Id, Arg.Any<PlaybackContext>()).Returns(start.Task);
        using var service = Create();
        await service.PlayAsync([a.Id]);
        await service.StopAsync();
        start.SetResult(9);
        await service.FlushPendingFinalizationAsync();
        await _library.Received(1).FinalizeListenSessionAsync(9, Arg.Any<TimeSpan>(), Arg.Any<PlaybackEndReason>());
    }

    [Fact]
    public async Task PlaybackEnded_FallbackDoesNotWaitForBlockedFinalization()
    {
        var (a, b, _) = Songs();
        ConfigureSongs(a, b, null);
        _library.StartListenSessionAsync(b.Id, Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(2));
        var finalize = Gate<object?>();
        _library.FinalizeListenSessionAsync(1, Arg.Any<TimeSpan>(), Arg.Any<PlaybackEndReason>()).Returns(finalize.Task);
        using var service = Create();
        try
        {
            await service.PlayAsync([a.Id, b.Id]);
            _audio.PlaybackEnded += Raise.Event<Action>();
            await Eventually(() => service.CurrentTrack?.Id == b.Id);
            await _audio.Received().LoadAsync(Arg.Is<Song>(s => s.Id == b.Id));
            await _audio.Received().PlayAsync();
        }
        finally
        {
            finalize.SetResult(null);
            await service.FlushPendingFinalizationAsync();
        }
    }

    [Fact]
    public async Task PlaybackEnded_ThrowingFinalizationStillFallsBack()
    {
        var (a, b, _) = Songs();
        ConfigureSongs(a, b, null);
        _library.StartListenSessionAsync(b.Id, Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(2));
        _library.FinalizeListenSessionAsync(1, Arg.Any<TimeSpan>(), Arg.Any<PlaybackEndReason>())
            .Returns(Task.FromException(new InvalidOperationException("finalize")));
        using var service = Create();
        await service.PlayAsync([a.Id, b.Id]);
        _audio.PlaybackEnded += Raise.Event<Action>();
        await Eventually(() => service.CurrentTrack?.Id == b.Id);
        await _audio.Received().LoadAsync(Arg.Is<Song>(s => s.Id == b.Id));
    }

    [Fact]
    public async Task ManualSelectionBeatsBlockedFallbackMetadata()
    {
        var (a, b, c) = Songs();
        var bGate = Gate<Song?>();
        ConfigureSongs(a, b, c);
        _library.GetSongByIdAsync(b.Id).Returns(bGate.Task);
        using var service = Create();
        try
        {
            await service.PlayAsync([a.Id, b.Id, c.Id]);
            _audio.PlaybackEnded += Raise.Event<Action>();
            await service.PlayQueueItemAsync(2).WaitAsync(TimeSpan.FromSeconds(2));
            service.CurrentTrack!.Id.Should().Be(c.Id);
            bGate.SetResult(b);
            await service.FlushPendingFinalizationAsync();
            await _audio.DidNotReceive().LoadAsync(Arg.Is<Song>(s => s.Id == b.Id));
            Received.PlayCount(_audio).Should().Be(2, "only A and the manually chosen C should start");
        }
        finally
        {
            bGate.TrySetResult(b);
            await service.FlushPendingFinalizationAsync();
        }
    }

    [Fact]
    public async Task ManualNextWhileMetadataPending_IgnoresOldPreparedEvent()
    {
        var (a, b, c) = Songs();
        ConfigureSongs(a, b, c);
        var preparation = CapturePreparation();
        using var service = Create();
        await service.PlayAsync([a.Id, b.Id, c.Id]);
        await Eventually(() => preparation.Count == 1);
        var oldPreparationId = preparation.First().Id;
        var bGate = Gate<Song?>();
        _library.GetSongByIdAsync(b.Id).Returns(bGate.Task);
        try
        {
            var manualNext = service.NextAsync();
            _audio.PreparedTrackStarted += Raise.Event<Action<long>>(oldPreparationId);
            bGate.SetResult(b);
            await manualNext.WaitAsync(TimeSpan.FromSeconds(2));
            service.CurrentTrack!.Id.Should().Be(b.Id);
            await _audio.DidNotReceive().LoadAsync(Arg.Is<Song>(s => s.Id == c.Id));
        }
        finally
        {
            bGate.TrySetResult(b);
            await service.FlushPendingFinalizationAsync();
        }
    }

    [Fact]
    public async Task RemovingPreparedB_CancelsItAndPreparesC()
    {
        var (a, b, c) = Songs();
        ConfigureSongs(a, b, c);
        var preparation = CapturePreparation();
        using var service = Create();
        await service.PlayAsync([a.Id, b.Id, c.Id]);
        await Eventually(() => preparation.Count == 1);
        var old = preparation.First();

        await service.RemoveFromQueueAsync(b.Id);
        await Eventually(() => preparation.Any(x => x.Song.Id == c.Id));

        old.Token.IsCancellationRequested.Should().BeTrue();
        _audio.PreparedTrackStarted += Raise.Event<Action<long>>(old.Id);
        service.CurrentTrack!.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task ShufflePreparationMatchesShuffledSuccessorAndCancelsOnReplacement()
    {
        var (a, b, c) = Songs();
        ConfigureSongs(a, b, c);
        _settings.GetInitialShuffleStateAsync().Returns(true);
        var preparation = CapturePreparation();
        using var service = Create();
        await service.InitializeAsync(false);
        await service.PlayAsync([a.Id, b.Id, c.Id]);
        await Eventually(() => preparation.Count == 1
            || service.CurrentShuffledIndex == service.ShuffledQueue.Count - 1);

        var currentShuffleIndex = service.CurrentShuffledIndex;
        if (currentShuffleIndex + 1 < service.ShuffledQueue.Count)
        {
            preparation.First().Song.Id.Should().Be(service.ShuffledQueue[currentShuffleIndex + 1]);
        }
        else
        {
            preparation.Should().BeEmpty();
        }

        if (preparation.TryPeek(out var prepared))
        {
            await service.RemoveFromQueueAsync(prepared.Song.Id);
            prepared.Token.IsCancellationRequested.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DelayedHistoryStart_StillMarksAEligibleAfterSkippingToB()
    {
        var (a, b, _) = Songs();
        a.Duration = TimeSpan.FromMinutes(3);
        ConfigureSongs(a, b, null);
        var startA = Gate<long?>();
        _library.StartListenSessionAsync(a.Id, Arg.Any<PlaybackContext>()).Returns(startA.Task);
        _library.StartListenSessionAsync(b.Id, Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(2));
        var eligibleSong = default(Song);
        long eligibleId = 0;
        _library.MarkListenAsEligibleForScrobblingAsync(Arg.Any<long>()).Returns(Task.FromResult(true));
        using var service = Create();
        service.ScrobbleEligibilityReached += (song, id, _) =>
        {
            eligibleSong = song;
            eligibleId = id;
        };
        try
        {
            _audio.Duration.Returns(TimeSpan.FromMinutes(3));
            await service.PlayAsync([a.Id, b.Id]);
            _audio.CurrentPosition.Returns(TimeSpan.FromSeconds(91));
            _audio.PositionChanged += Raise.Event<Action>();
            await service.PlayQueueItemAsync(1);
            startA.SetResult(1);
            await service.FlushPendingFinalizationAsync();

            await _library.Received(1).MarkListenAsEligibleForScrobblingAsync(1);
            eligibleSong.Should().BeSameAs(a);
            eligibleId.Should().Be(1);
        }
        finally
        {
            startA.TrySetResult(1);
            await service.FlushPendingFinalizationAsync();
        }
    }

    private MusicPlaybackService Create() => new(_settings, _audio, _library, _metadata, _logger);

    private void ConfigureSongs(Song a, Song b, Song? c, Task<Song?>? bResult = null)
    {
        _library.GetSongByIdAsync(a.Id).Returns(a);
        _library.GetSongByIdAsync(b.Id).Returns(bResult ?? Task.FromResult<Song?>(b));
        if (c != null) _library.GetSongByIdAsync(c.Id).Returns(c);
        _library.StartListenSessionAsync(Arg.Any<Guid>(), Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(1));
    }

    private ConcurrentQueue<PreparedCall> CapturePreparation()
    {
        var calls = new ConcurrentQueue<PreparedCall>();
        _audio.PrepareNextAsync(Arg.Any<Song>(), Arg.Any<long>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => calls.Enqueue(new PreparedCall(call.Arg<Song>(), call.Arg<long>(), call.Arg<CancellationToken>())));
        return calls;
    }

    private static (Song A, Song B, Song C) Songs() =>
        (new() { Id = Guid.NewGuid(), Title = "A", FilePath = "a.mp3", Duration = TimeSpan.FromSeconds(10) },
            new() { Id = Guid.NewGuid(), Title = "B", FilePath = "b.mp3", Duration = TimeSpan.FromSeconds(20) },
            new() { Id = Guid.NewGuid(), Title = "C", FilePath = "c.mp3", Duration = TimeSpan.FromSeconds(30) });

    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
        condition().Should().BeTrue();
    }
    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100 && !await condition(); i++) await Task.Delay(10);
        (await condition()).Should().BeTrue();
    }
    private sealed record PreparedCall(Song Song, long Id, CancellationToken Token);
    private static class Received
    {
        public static int LoadCount(IAudioPlayer audio) => audio.ReceivedCalls().Count(x => x.GetMethodInfo().Name == nameof(IAudioPlayer.LoadAsync));
        public static int PlayCount(IAudioPlayer audio) => audio.ReceivedCalls().Count(x => x.GetMethodInfo().Name == nameof(IAudioPlayer.PlayAsync));
    }
}
