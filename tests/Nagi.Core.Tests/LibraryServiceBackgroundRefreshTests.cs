using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nagi.Core.Helpers;
using Nagi.Core.Http.Pipelines;
using Nagi.Core.Models;
using Nagi.Core.Services.Abstractions;
using Nagi.Core.Services.Data;
using Nagi.Core.Services.Implementations;
using Nagi.Core.Tests.Utils;
using NSubstitute;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class LibraryServiceBackgroundRefreshTests : IDisposable
{
    private readonly DbContextFactoryTestHelper _dbHelper = new(useFileDatabase: true);
    private readonly IFileSystemService _fileSystem = Substitute.For<IFileSystemService>();
    private readonly IMetadataService _metadataService = Substitute.For<IMetadataService>();
    private readonly ProviderPipelineProvider _pipelines = TestProviderPipeline.Build(ServiceProviderIds.ImageDownload);
    private readonly LibraryService _libraryService;
    private readonly List<(string Path, DateTime LastWriteTimeUtc)> _diskEntries = [];

    public LibraryServiceBackgroundRefreshTests()
    {
        _fileSystem.EnumerateFilesWithLastWriteTime(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns(_ => _diskEntries);
        _fileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>()).Returns([]);
        _fileSystem.GetExtension(Arg.Any<string>()).Returns(call => Path.GetExtension(call.ArgAt<string>(0)));
        _fileSystem.GetDirectoryName(Arg.Any<string>()).Returns(call => Path.GetDirectoryName(call.ArgAt<string>(0)));
        _fileSystem.GetFileNameWithoutExtension(Arg.Any<string>()).Returns(call => Path.GetFileNameWithoutExtension(call.ArgAt<string>(0)));
        _fileSystem.IsNetworkPath(Arg.Any<string>()).Returns(false);

        _libraryService = new LibraryService(
            _dbHelper.ContextFactory, _fileSystem, _metadataService,
            Substitute.For<IMusicBrainzService>(), Substitute.For<IHttpClientFactory>(),
            Substitute.For<IServiceScopeFactory>(), Substitute.For<IPathConfiguration>(),
            Substitute.For<ISettingsService>(), Substitute.For<IReplayGainService>(),
            Substitute.For<IImageProcessor>(), _pipelines, Substitute.For<ILogger<LibraryService>>());
    }

    public void Dispose()
    {
        _libraryService.Dispose();
        _pipelines.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _dbHelper.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RefreshFolderInBackground_WhenRootIsUnreachable_PreservesFolderAndSongs()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Name = "Music", Path = @"C:\Music" };
        var song = new Song { FolderId = folder.Id, FilePath = @"C:\Music\old.flac", DirectoryPath = folder.Path, Title = "Old" };
        await SeedAsync(folder, song);
        _fileSystem.DirectoryExists(folder.Path).Returns(false);

        (await _libraryService.RefreshFolderInBackgroundAsync(folder.Id)).Should().BeFalse();
        await using var context = _dbHelper.ContextFactory.CreateDbContext();
        (await context.Folders.AnyAsync(f => f.Id == folder.Id)).Should().BeTrue();
        (await context.Songs.AnyAsync(s => s.Id == song.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task RefreshFolderInBackground_AddsNestedFileWithRootScopeAndDirectoryPath()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Name = "Music", Path = @"C:\Music" };
        var nested = Path.Combine(folder.Path, "Artist", "Album", "new.flac");
        var modified = DateTime.UtcNow;
        await SeedAsync(folder);
        _fileSystem.DirectoryExists(folder.Path).Returns(true);
        _diskEntries.Add((nested, modified));
        _metadataService.ExtractMetadataAsync(nested, Arg.Any<string?>(), Arg.Any<bool>()).Returns(new SongFileMetadata
        {
            FilePath = nested, FileModifiedDate = modified, Title = "New", Artists = ["Artist"],
            AlbumArtists = ["Artist"], Album = "Album", TrackNumber = 1
        });

        (await _libraryService.RefreshFolderInBackgroundAsync(folder.Id)).Should().BeTrue();
        await using var context = _dbHelper.ContextFactory.CreateDbContext();
        var added = await context.Songs.SingleAsync(s => s.FilePath == nested);
        added.FolderId.Should().Be(folder.Id);
        added.DirectoryPath.Should().Be(Path.GetDirectoryName(nested));
    }

    [Fact]
    public async Task RefreshFolderInBackground_WhenCancelled_PreservesRoot()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Name = "Music", Path = @"C:\Music" };
        await SeedAsync(folder);
        _fileSystem.DirectoryExists(folder.Path).Returns(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        (await _libraryService.RefreshFolderInBackgroundAsync(folder.Id, cancellation.Token)).Should().BeFalse();
        await using var context = _dbHelper.ContextFactory.CreateDbContext();
        (await context.Folders.AnyAsync(f => f.Id == folder.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task RefreshFolderInBackground_DoesNotCancelAnActiveManualScan()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Name = "Music", Path = @"C:\Music" };
        var filePath = Path.Combine(folder.Path, "new.flac");
        await SeedAsync(folder);
        _fileSystem.DirectoryExists(folder.Path).Returns(true);
        _diskEntries.Add((filePath, DateTime.UtcNow));
        var enteredMetadata = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadataGate = new TaskCompletionSource<SongFileMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        var validMetadata = new SongFileMetadata
        {
            FilePath = filePath, FileModifiedDate = _diskEntries[0].LastWriteTimeUtc,
            Title = "New", Artists = ["Artist"], AlbumArtists = ["Artist"], Album = "Album"
        };
        _metadataService.ExtractMetadataAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>())
            .Returns(_ =>
            {
                enteredMetadata.TrySetResult(true);
                return metadataGate.Task;
            });

        var manual = _libraryService.RescanFolderForMusicAsync(folder.Id);
        var background = _libraryService.RefreshFolderInBackgroundAsync(folder.Id);
        try
        {
            await enteredMetadata.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(() => manual.WaitAsync(TimeSpan.FromMilliseconds(100)));
            metadataGate.TrySetResult(validMetadata);
            (await manual.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
            await background.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            metadataGate.TrySetResult(validMetadata);
            try { await manual.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            try { await background.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }
        await using var context = _dbHelper.ContextFactory.CreateDbContext();
        (await context.Songs.CountAsync(s => s.FolderId == folder.Id)).Should().Be(1);
    }

    private async Task SeedAsync(Folder folder, params Song[] songs)
    {
        await using var context = _dbHelper.ContextFactory.CreateDbContext();
        context.Folders.Add(folder);
        context.Songs.AddRange(songs);
        await context.SaveChangesAsync();
    }
}
