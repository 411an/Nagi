using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Implementations;
using NSubstitute;
using Xunit;

namespace Nagi.Core.Tests;

public class PlaybackListenSessionTests
{
    [Fact]
    public async Task PendingStart_ConcurrentEnds_FinalizesOnceWithFirstArguments()
    {
        var start = NewGate<long?>();
        var library = Substitute.For<ILibraryService>();
        library.StartListenSessionAsync(Arg.Any<Guid>(), Arg.Any<PlaybackContext>()).Returns(start.Task);
        var session = Create(library);
        var firstPosition = TimeSpan.FromSeconds(12);

        var first = session.EndAsync(firstPosition, PlaybackEndReason.Skipped);
        var second = session.EndAsync(TimeSpan.FromSeconds(99), PlaybackEndReason.Finished);
        first.Should().BeSameAs(second);
        session.Id.Should().BeNull();

        start.SetResult(42);
        await first;

        await library.Received(1).FinalizeListenSessionAsync(42, firstPosition, PlaybackEndReason.Skipped);
    }

    [Fact]
    public async Task NullStartId_DoesNotFinalize()
    {
        var library = Substitute.For<ILibraryService>();
        library.StartListenSessionAsync(Arg.Any<Guid>(), Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(null));
        var session = Create(library);

        await session.EndAsync(TimeSpan.FromSeconds(1), PlaybackEndReason.Finished);

        session.Id.Should().BeNull();
        await library.DidNotReceive().FinalizeListenSessionAsync(Arg.Any<long>(), Arg.Any<TimeSpan>(), Arg.Any<PlaybackEndReason>());
    }

    [Fact]
    public async Task StartFailure_IsSwallowedAndDoesNotFinalize()
    {
        var library = Substitute.For<ILibraryService>();
        library.StartListenSessionAsync(Arg.Any<Guid>(), Arg.Any<PlaybackContext>())
            .Returns(Task.FromException<long?>(new InvalidOperationException("start")));
        var session = Create(library);

        var act = async () => await session.EndAsync(TimeSpan.Zero, PlaybackEndReason.Error);
        await act.Should().NotThrowAsync();

        session.Started.Status.Should().Be(TaskStatus.RanToCompletion);
        await library.DidNotReceive().FinalizeListenSessionAsync(Arg.Any<long>(), Arg.Any<TimeSpan>(), Arg.Any<PlaybackEndReason>());
    }

    [Fact]
    public async Task FinalizeFailure_IsSwallowedAndNotRetried()
    {
        var library = Substitute.For<ILibraryService>();
        library.StartListenSessionAsync(Arg.Any<Guid>(), Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(7));
        library.FinalizeListenSessionAsync(7, Arg.Any<TimeSpan>(), Arg.Any<PlaybackEndReason>())
            .Returns(Task.FromException(new InvalidOperationException("end")));
        var session = Create(library);

        await session.EndAsync(TimeSpan.FromSeconds(2), PlaybackEndReason.Skipped);
        await session.EndAsync(TimeSpan.FromSeconds(3), PlaybackEndReason.Finished);

        await library.Received(1).FinalizeListenSessionAsync(7, TimeSpan.FromSeconds(2), PlaybackEndReason.Skipped);
    }

    [Fact]
    public async Task SuccessfulStart_ExposesIdAndStartTime()
    {
        var library = Substitute.For<ILibraryService>();
        library.StartListenSessionAsync(Arg.Any<Guid>(), Arg.Any<PlaybackContext>()).Returns(Task.FromResult<long?>(123));
        var before = DateTime.UtcNow;
        var session = Create(library);

        (await session.Started).Should().Be(123);
        session.Id.Should().Be(123);
        session.StartedUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    private static PlaybackListenSession Create(ILibraryService library) =>
        new(library, Guid.NewGuid(), new PlaybackContext(PlaybackContextType.Library, null), Substitute.For<ILogger>());

    private static TaskCompletionSource<T> NewGate<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
