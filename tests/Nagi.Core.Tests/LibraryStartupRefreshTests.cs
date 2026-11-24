using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Implementations;
using NSubstitute;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class LibraryStartupRefreshTests
{
    [Fact]
    public async Task StartAsync_RefreshesEachAvailableRootOnce_EvenWhenCalledConcurrently()
    {
        var h = new Harness(["C:\\Music", "D:\\Audio"]);
        await using var refresh = h.Create();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => refresh.StartAsync()));
        await refresh.StartAsync();

        h.Refreshes.Should().BeEquivalentTo(h.Roots.Select(root => root.Id));
        await h.Library.Received(1).GetRootFoldersAsync(Arg.Any<CancellationToken>());
        foreach (var root in h.Roots)
            await h.Library.Received(1).RefreshFolderInBackgroundAsync(root.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SkipsUnavailableRoot()
    {
        var h = new Harness(["C:\\Missing", "D:\\Audio"]);
        h.FileSystem.DirectoryExists("C:\\Missing").Returns(false);
        await using var refresh = h.Create();
        await refresh.StartAsync();

        h.Refreshes.Should().Equal(h.Roots[1].Id);
        await h.Library.DidNotReceive().RefreshFolderInBackgroundAsync(h.Roots[0].Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ContinuesAfterRootFailureWithoutRetrying()
    {
        var h = new Harness(["C:\\Music", "D:\\Audio"]);
        h.Library.RefreshFolderInBackgroundAsync(h.Roots[0].Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<bool>(new InvalidOperationException("failed")));
        await using var refresh = h.Create();
        await refresh.StartAsync();

        await h.Library.Received(1).RefreshFolderInBackgroundAsync(h.Roots[0].Id, Arg.Any<CancellationToken>());
        await h.Library.Received(1).RefreshFolderInBackgroundAsync(h.Roots[1].Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_CancelsBlockedRefreshAndDoesNotStartNextRoot()
    {
        var h = new Harness(["C:\\Music", "D:\\Audio"]);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Library.RefreshFolderInBackgroundAsync(h.Roots[0].Id, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                entered.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, call.ArgAt<CancellationToken>(1));
                return true;
            });
        await using var refresh = h.Create();
        var start = refresh.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await refresh.StopAsync();

        await h.Library.DidNotReceive().RefreshFolderInBackgroundAsync(h.Roots[1].Id, Arg.Any<CancellationToken>());
        start.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Lifecycle_IsIdempotentAndStartAfterDisposeThrows()
    {
        var h = new Harness(["C:\\Music"]);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Library.RefreshFolderInBackgroundAsync(h.Roots[0].Id, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                entered.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, call.ArgAt<CancellationToken>(1));
                return true;
            });
        await using var refresh = h.Create();
        _ = refresh.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await refresh.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await refresh.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await refresh.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await refresh.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var act = async () => await refresh.StartAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task StopAsync_BeforeStartPreventsWork()
    {
        var h = new Harness(["C:\\Music"]);
        await using var refresh = h.Create();
        await refresh.StopAsync();
        await refresh.StartAsync();

        await h.Library.DidNotReceive().GetRootFoldersAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompletedPass_DoesNotRespondToLaterFilesystemChangesOrEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "nagi-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var h = new Harness([root]);
            await using var refresh = h.Create();
            await refresh.StartAsync();
            var before = h.Refreshes.Count;
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "new.wav"), "audio");
            File.WriteAllText(Path.Combine(root, "new.cue"), "cue");
            h.Library.LibraryContentChanged += Raise.Event<EventHandler<Nagi.Core.Services.Data.LibraryContentChangedEventArgs>>(
                h.Library, new Nagi.Core.Services.Data.LibraryContentChangedEventArgs(Nagi.Core.Services.Data.LibraryChangeType.LibraryRescanned));
            await Task.Delay(TimeSpan.FromSeconds(4.5));
            h.Refreshes.Count.Should().Be(before);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Harness
    {
        public readonly ILibraryService Library = Substitute.For<ILibraryService>();
        public readonly IFileSystemService FileSystem = Substitute.For<IFileSystemService>();
        public readonly List<Folder> Roots;
        public readonly ConcurrentBag<Guid> Refreshes = [];

        public Harness(string[] paths)
        {
            Roots = paths.Select(path => new Folder { Id = Guid.NewGuid(), Name = path, Path = path }).ToList();
            Library.GetRootFoldersAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Folder>>(Roots));
            Library.RefreshFolderInBackgroundAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => { Refreshes.Add(call.ArgAt<Guid>(0)); return Task.FromResult(true); });
            FileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        }

        public LibraryStartupRefresh Create() => new(Library, FileSystem, Substitute.For<ILogger<LibraryStartupRefresh>>());
    }
}
