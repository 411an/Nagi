using FluentAssertions;
using Nagi.Core.Helpers;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class LibraryFolderWatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nagi-watcher-" + Guid.NewGuid().ToString("N"));

    public LibraryFolderWatcherTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("track.flac", true)]
    [InlineData("track.CUE", true)]
    [InlineData("track.txt", false)]
    [InlineData("track", false)]
    public void IsRelevantFile_RecognizesSupportedExtensions(string name, bool expected)
    {
        LibraryFolderWatcher.IsRelevantFile(Path.Combine(_root, name)).Should().Be(expected);
    }

    [Fact]
    public async Task Watcher_ReportsDottedDirectoryCreation()
    {
        var callback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = NewWatcher(_root, callback);

        var nested = Path.Combine(_root, "edition.with.dots");
        Directory.CreateDirectory(nested);
        await callback.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Watcher_ReportsAudioFileCreationInExistingNestedDirectory()
    {
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        var callback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = NewWatcher(_root, callback);

        File.WriteAllText(Path.Combine(nested, "track.flac"), "audio");
        await callback.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Watcher_IgnoresTextFileCreation()
    {
        var callback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = NewWatcher(_root, callback);

        File.WriteAllText(Path.Combine(_root, "notes.txt"), "text");
        var completed = await Task.WhenAny(callback.Task, Task.Delay(500));
        completed.Should().NotBe(callback.Task);
    }

    [Fact]
    public async Task Watcher_ReportsRenameWhenNewExtensionIsSupported()
    {
        var temporary = Path.Combine(_root, "track.tmp");
        File.WriteAllText(temporary, "audio");
        var callback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = NewWatcher(_root, callback);

        File.Move(temporary, Path.ChangeExtension(temporary, ".flac"));
        await callback.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dispose_StopsCallbacks()
    {
        var callbackCount = 0;
        var errors = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var watcher = new LibraryFolderWatcher(
                   _root,
                   () => Interlocked.Increment(ref callbackCount),
                   _ => errors.TrySetResult(true)))
            watcher.Dispose();

        Directory.CreateDirectory(Path.Combine(_root, "after-dispose"));
        File.WriteAllText(Path.Combine(_root, "after-dispose", "track.flac"), "audio");
        await Task.Delay(300);

        Volatile.Read(ref callbackCount).Should().Be(0);
        errors.Task.IsCompleted.Should().BeFalse();
    }

    private static LibraryFolderWatcher NewWatcher(string path, TaskCompletionSource<bool> callback)
    {
        return new LibraryFolderWatcher(
            path,
            () => callback.TrySetResult(true),
            error => callback.TrySetException(error));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
