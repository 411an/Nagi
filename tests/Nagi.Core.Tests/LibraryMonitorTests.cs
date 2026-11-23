using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Data;
using Nagi.Core.Services.Implementations;
using NSubstitute;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class LibraryMonitorTests
{
    [Fact]
    public async Task Start_RefreshesEachAvailableRootAndCreatesWatchers()
    {
        await using var h = new Harness(["C:\\Music", "D:\\Audio"]);
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(50));
        monitor.Start();

        await EventuallyAsync(() => h.Refreshes.Count == 2);
        h.Refreshes.Should().BeEquivalentTo(h.Roots.Select(r => r.Id));
        h.Watchers.Keys.Should().BeEquivalentTo(h.Roots.Select(r => r.Path));
    }

    [Fact]
    public async Task ChangedBurst_IsDebouncedToOneAdditionalRefresh()
    {
        await using var h = new Harness(["C:\\Music"]);
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(50));
        monitor.Start();
        await EventuallyAsync(() => h.Refreshes.Count == 1);
        h.Watchers["C:\\Music"].Changed!();
        h.Watchers["C:\\Music"].Changed!();
        h.Watchers["C:\\Music"].Changed!();
        await EventuallyAsync(() => h.Refreshes.Count == 2);
        await Task.Delay(100);
        h.Refreshes.Count.Should().Be(2);
    }

    [Fact]
    public async Task EventWhileScanIsRunning_IsNotLost()
    {
        await using var h = new Harness(["C:\\Music"]);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        h.Library.RefreshFolderInBackgroundAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Interlocked.Increment(ref calls) == 1
                ? gate.Task.WaitAsync(call.ArgAt<CancellationToken>(1))
                : Task.FromResult(true));
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(20));
        monitor.Start();
        await EventuallyAsync(() => calls == 1);
        h.Watchers["C:\\Music"].Changed!();
        gate.SetResult(true);
        await EventuallyAsync(() => calls == 2);
    }

    [Fact]
    public async Task EventForOneRoot_DoesNotRefreshAnotherRoot()
    {
        await using var h = new Harness(["C:\\Music", "D:\\Audio"]);
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(30));
        monitor.Start();
        await EventuallyAsync(() => h.Refreshes.Count == 2);
        h.Refreshes.Clear();
        h.Watchers["C:\\Music"].Changed!();
        await EventuallyAsync(() => h.Refreshes.Count == 1);
        h.Refreshes.Should().Equal(h.Roots[0].Id);
    }

    [Fact]
    public async Task RemovedRoot_DisposesWatcherAndIgnoresLateCallback()
    {
        await using var h = new Harness(["C:\\Music"]);
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(30));
        monitor.Start();
        await EventuallyAsync(() => h.Watchers.Count == 1);
        var watcher = h.Watchers["C:\\Music"];
        h.Library.GetRootFoldersAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Folder>>([]));
        h.Library.LibraryContentChanged += Raise.Event<EventHandler<LibraryContentChangedEventArgs>>(
            h.Library, new LibraryContentChangedEventArgs(LibraryChangeType.FolderRemoved));
        await EventuallyAsync(() => watcher.Disposed);
        var before = h.Refreshes.Count;
        watcher.Changed!();
        await Task.Delay(100);
        h.Refreshes.Count.Should().Be(before);
    }

    [Fact]
    public async Task StopAsync_DisposesWatchersAndPreventsFurtherRefresh()
    {
        await using var h = new Harness(["C:\\Music"]);
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(30));
        monitor.Start();
        await EventuallyAsync(() => h.Refreshes.Count == 1);
        await monitor.StopAsync();
        h.Watchers.Values.Should().OnlyContain(w => w.Disposed);
        h.Watchers["C:\\Music"].Changed!();
        await Task.Delay(100);
        h.Refreshes.Count.Should().Be(1);
    }

    [Fact]
    public async Task UnavailableRoot_IsRetriedAndWatchedWhenItReappears()
    {
        await using var h = new Harness(["C:\\Music"]);
        h.FileSystem.DirectoryExists("C:\\Music").Returns(false);
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(30));
        monitor.Start();
        await Task.Delay(100);
        h.Watchers.Should().BeEmpty();
        h.Refreshes.Should().BeEmpty();
        h.FileSystem.DirectoryExists("C:\\Music").Returns(true);
        h.Library.LibraryContentChanged += Raise.Event<EventHandler<LibraryContentChangedEventArgs>>(
            h.Library, new LibraryContentChangedEventArgs(LibraryChangeType.LibraryRescanned));
        await EventuallyAsync(() => h.Watchers.ContainsKey("C:\\Music"));
        await EventuallyAsync(() => h.Refreshes.Count == 1);
    }

    [Fact]
    public async Task WatcherError_RecreatesWatcherAndRefreshesRoot()
    {
        await using var h = new Harness(["C:\\Music"]);
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(30));
        monitor.Start();
        await EventuallyAsync(() => h.Watchers.Count == 1 && h.Refreshes.Count == 1);
        var first = h.Watchers["C:\\Music"];
        first.Error!(new IOException("watch failed"));
        await EventuallyAsync(() => h.Watchers.Values.Any(w => !ReferenceEquals(w, first)));
        await EventuallyAsync(() => h.Refreshes.Count >= 2);
        first.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task TransientRefreshFailure_IsRetried()
    {
        await using var h = new Harness(["C:\\Music"]);
        var calls = 0;
        h.Library.RefreshFolderInBackgroundAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(true);
            });
        await using var monitor = h.Create(TimeSpan.FromMilliseconds(20));
        monitor.Start();
        await EventuallyAsync(() => Volatile.Read(ref calls) >= 2);
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < until)
            await Task.Delay(10);
        condition().Should().BeTrue("condition did not become true before timeout");
    }

    private sealed class Harness : IAsyncDisposable
    {
        public readonly ILibraryService Library = Substitute.For<ILibraryService>();
        public readonly IFileSystemService FileSystem = Substitute.For<IFileSystemService>();
        public readonly List<Folder> Roots;
        public readonly ConcurrentDictionary<string, FakeWatcher> Watchers = new(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentBag<Guid> Refreshes = [];

        public Harness(string[] paths)
        {
            Roots = paths.Select(path => new Folder { Id = Guid.NewGuid(), Name = path, Path = path }).ToList();
            Library.GetRootFoldersAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Folder>>(Roots));
            Library.RefreshFolderInBackgroundAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => { Refreshes.Add(call.ArgAt<Guid>(0)); return Task.FromResult(true); });
            FileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        }

        public LibraryMonitor Create(TimeSpan quiet)
        {
            return new LibraryMonitor(Library, FileSystem, Substitute.For<ILogger<LibraryMonitor>>(),
                (path, changed, error) => Watchers[path] = new FakeWatcher(changed, error),
                quiet, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeWatcher(Action changed, Action<Exception> error) : IDisposable
    {
        public Action? Changed { get; } = changed;
        public Action<Exception>? Error { get; } = error;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
