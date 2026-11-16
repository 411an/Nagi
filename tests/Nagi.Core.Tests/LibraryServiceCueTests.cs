using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nagi.Core.Data;
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

public sealed class LibraryServiceCueTests : IDisposable
{
    private readonly DbContextFactoryTestHelper _dbHelper = new(useFileDatabase: true);
    private readonly IFileSystemService _fileSystem = Substitute.For<IFileSystemService>();
    private readonly IMetadataService _metadataService = Substitute.For<IMetadataService>();
    private readonly ProviderPipelineProvider _pipelines = TestProviderPipeline.Build(ServiceProviderIds.ImageDownload);
    private readonly LibraryService _libraryService;
    private readonly List<(string Path, DateTime LastWriteTimeUtc)> _diskEntries = [];
    private SongFileMetadata _metadata = null!;

    public LibraryServiceCueTests()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var pathConfiguration = Substitute.For<IPathConfiguration>();
        var settingsService = Substitute.For<ISettingsService>();
        var replayGainService = Substitute.For<IReplayGainService>();
        var musicBrainzService = Substitute.For<IMusicBrainzService>();
        var imageProcessor = Substitute.For<IImageProcessor>();
        var logger = Substitute.For<ILogger<LibraryService>>();

        _libraryService = new LibraryService(
            _dbHelper.ContextFactory,
            _fileSystem,
            _metadataService,
            musicBrainzService,
            httpClientFactory,
            scopeFactory,
            pathConfiguration,
            settingsService,
            replayGainService,
            imageProcessor,
            _pipelines,
            logger);

        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        _fileSystem.EnumerateFilesWithLastWriteTime(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns(_ => _diskEntries);
        _fileSystem.IsNetworkPath(Arg.Any<string>()).Returns(false);
        _fileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>()).Returns([]);
        _fileSystem.GetExtension(Arg.Any<string>())
            .Returns(call => Path.GetExtension(call.ArgAt<string>(0)));
        _fileSystem.GetDirectoryName(Arg.Any<string>())
            .Returns(call => Path.GetDirectoryName(call.ArgAt<string>(0)));
        _fileSystem.GetFileNameWithoutExtension(Arg.Any<string>())
            .Returns(call => Path.GetFileNameWithoutExtension(call.ArgAt<string>(0)));
        _metadataService.ExtractMetadataAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>())
            .Returns(_ => _metadata);
    }

    public void Dispose()
    {
        _libraryService.Dispose();
        _pipelines.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _dbHelper.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Rescan_CueTracks_ReusesWholeFileRowAndReconcilesByTrackNumber()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Path = @"C:\Music\Cue", Name = "Cue" };
        var audioPath = Path.Combine(folder.Path, "album.flac");
        var cuePath = Path.Combine(folder.Path, "album.cue");
        var audioTime = DateTime.UtcNow.AddHours(-2);
        var cueTime = DateTime.UtcNow.AddHours(-1);
        var originalId = Guid.NewGuid();
        var playlist = new Playlist { Name = "Favorites" };
        var original = new Song
        {
            Id = originalId,
            FolderId = folder.Id,
            FilePath = audioPath,
            DirectoryPath = folder.Path,
            FileModifiedDate = audioTime,
            Title = "Whole file"
        };
        playlist.PlaylistSongs.Add(new PlaylistSong { Song = original, Order = 1 });

        await using (var context = _dbHelper.ContextFactory.CreateDbContext())
        {
            context.Folders.Add(folder);
            context.Playlists.Add(playlist);
            await context.SaveChangesAsync();
        }

        _diskEntries.Add((audioPath, audioTime));
        _diskEntries.Add((cuePath, cueTime));
        _metadata = CreateFileMetadata(audioPath, cuePath, cueTime,
            audioTime, (1, "One", 0, 10), (2, "Two", 10, 15), (3, "Three", 25, 20));

        (await _libraryService.RescanFolderForMusicAsync(folder.Id)).Should().BeTrue();

        var firstIds = await AssertCueRowsAsync(folder.Id, originalId, playlist.Id, new[] { 0d, 10d, 25d });
        firstIds.Should().HaveCount(3);

        _metadataService.ClearReceivedCalls();
        (await _libraryService.RescanFolderForMusicAsync(folder.Id)).Should().BeFalse();
        _metadataService.ReceivedCalls().Should().BeEmpty("unchanged audio and CUE timestamps need no extraction");
        (await ReadSongIdsAsync(audioPath)).Should().Equal(firstIds);

        cueTime = cueTime.AddMinutes(1);
        _diskEntries[1] = (cuePath, cueTime);
        _metadata = CreateFileMetadata(audioPath, cuePath, cueTime,
            audioTime, (1, "One", 0, 12), (2, "Changed", 12, 18), (3, "Three", 30, 15));
        (await _libraryService.RescanFolderForMusicAsync(folder.Id)).Should().BeTrue();

        await using (var context = _dbHelper.ContextFactory.CreateDbContext())
        {
            var rows = await context.Songs.Where(s => s.FilePath == audioPath).OrderBy(s => s.CueTrackNumber).ToListAsync();
            rows.Select(s => s.Id).Should().BeEquivalentTo(firstIds);
            rows.Select(s => s.StartOffsetTicks).Should().BeEquivalentTo(new[]
            {
                TimeSpan.Zero.Ticks, TimeSpan.FromSeconds(12).Ticks, TimeSpan.FromSeconds(30).Ticks
            });
            rows.Select(s => s.DurationTicks).Should().BeEquivalentTo(new[]
            {
                TimeSpan.FromSeconds(12).Ticks, TimeSpan.FromSeconds(18).Ticks, TimeSpan.FromSeconds(15).Ticks
            });
            rows[1].Title.Should().Be("Changed");
        }

        (await _libraryService.RescanFolderForMusicAsync(folder.Id, true)).Should().BeTrue();
        await using (var context = _dbHelper.ContextFactory.CreateDbContext())
            (await context.Songs.Where(s => s.FilePath == audioPath).CountAsync()).Should().Be(3);

        _diskEntries.RemoveAt(1);
        _metadata = new SongFileMetadata
        {
            FilePath = audioPath,
            FileModifiedDate = audioTime,
            Title = "Whole file",
            Artists = ["Artist"],
            AlbumArtists = ["Artist"],
            Album = "Album"
        };
        (await _libraryService.RescanFolderForMusicAsync(folder.Id)).Should().BeTrue();
        await using (var context = _dbHelper.ContextFactory.CreateDbContext())
        {
            var row = await context.Songs.SingleAsync(s => s.FilePath == audioPath);
            row.Id.Should().Be(originalId);
            row.CueTrackNumber.Should().Be(0);
        }
    }

    [Fact]
    public async Task Rescan_WhenCueReadFails_PreservesExistingCueRows()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Path = @"C:\Music\CueFailure", Name = "CueFailure" };
        var audioPath = Path.Combine(folder.Path, "album.flac");
        var cuePath = Path.Combine(folder.Path, "album.cue");
        var audioTime = DateTime.UtcNow.AddHours(-2);
        var cueTime = DateTime.UtcNow.AddHours(-1);
        await using (var context = _dbHelper.ContextFactory.CreateDbContext())
        {
            context.Folders.Add(folder);
            await context.SaveChangesAsync();
        }

        _diskEntries.Add((audioPath, audioTime));
        _diskEntries.Add((cuePath, cueTime));
        _metadata = CreateFileMetadata(audioPath, cuePath, cueTime,
            audioTime, (1, "One", 0, 10), (2, "Two", 10, 15));
        (await _libraryService.RescanFolderForMusicAsync(folder.Id)).Should().BeTrue();
        var before = await ReadSongIdsAsync(audioPath);

        cueTime = cueTime.AddMinutes(1);
        _diskEntries[1] = (cuePath, cueTime);
        _metadata = new SongFileMetadata
        {
            FilePath = audioPath,
            FileModifiedDate = audioTime,
            CueFilePath = cuePath,
            CueFileModifiedDate = cueTime,
            CueReadFailed = true,
            CueTracks = []
        };
        (await _libraryService.RescanFolderForMusicAsync(folder.Id)).Should().BeTrue();

        (await ReadSongIdsAsync(audioPath)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task AddCueTracksMigration_PreservesLegacySongAndEnforcesCompositeIdentity()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"nagi-cue-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<MusicDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            var folderId = Guid.NewGuid();
            var songId = Guid.NewGuid();
            var playlistId = Guid.NewGuid();
            var filePath = @"C:\Music\Legacy\album.flac";
            var directoryPath = @"C:\Music\Legacy";
            var legacyName = "Legacy";
            var artistName = "Artist";
            var playlistName = "Legacy playlist";

            await using (var context = new MusicDbContext(options))
            {
                var migrator = context.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20260830194706_OptimizeSongTitleSortIndexes");
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO Folders (Id, Name, Path) VALUES ({folderId}, {legacyName}, {directoryPath});
                    INSERT INTO Songs (Id, Title, SortTitle, FolderId, DurationTicks, FilePath, DirectoryPath,
                        DateAddedToLibrary, IsLoved, PlayCount, SkipCount, TotalListenTimeTicks,
                        ArtistName, PrimaryArtistName, PrimaryArtistSortName)
                    VALUES ({songId}, {legacyName}, {legacyName}, {folderId}, {0L}, {filePath}, {directoryPath},
                        {DateTime.UtcNow}, {false}, {0}, {0}, {0L}, {artistName}, {artistName}, {artistName});
                    INSERT INTO Playlists (Id, Name, DateCreated, DateModified) VALUES ({playlistId}, {playlistName}, {DateTime.UtcNow}, {DateTime.UtcNow});
                    INSERT INTO PlaylistSongs (PlaylistId, SongId, "Order") VALUES ({playlistId}, {songId}, {1d});
                    """);
                await migrator.MigrateAsync("20261113090000_AddCueTracks");
            }

            await using (var context = new MusicDbContext(options))
            {
                var legacy = await context.Songs.SingleAsync(s => s.Id == songId);
                legacy.FilePath.Should().Be(filePath);
                legacy.CueTrackNumber.Should().Be(0);
                legacy.StartOffsetTicks.Should().Be(0);
                legacy.CueFilePath.Should().BeNull();
                (await context.PlaylistSongs.CountAsync(p => p.PlaylistId == playlistId && p.SongId == songId)).Should().Be(1);

                context.Songs.AddRange(
                    new Song { FolderId = folderId, FilePath = filePath, DirectoryPath = @"C:\Music\Legacy", CueTrackNumber = 1 },
                    new Song { FolderId = folderId, FilePath = filePath, DirectoryPath = @"C:\Music\Legacy", CueTrackNumber = 2 });
                await context.SaveChangesAsync();
                (await context.Songs.CountAsync(s => s.FilePath == filePath)).Should().Be(3);

                context.Songs.Add(new Song { FolderId = folderId, FilePath = filePath, DirectoryPath = @"C:\Music\Legacy", CueTrackNumber = 1 });
                await FluentActions.Invoking(async () => await context.SaveChangesAsync())
                    .Should().ThrowAsync<DbUpdateException>();
            }
        }
        finally
        {
            foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Snapshot_HasNoPendingModelChanges()
    {
        using var context = _dbHelper.ContextFactory.CreateDbContext();
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }

    private async Task<List<Guid>> AssertCueRowsAsync(Guid folderId, Guid originalId, Guid playlistId, double[] offsets)
    {
        await using var context = _dbHelper.ContextFactory.CreateDbContext();
        var rows = await context.Songs.Where(s => s.FolderId == folderId).OrderBy(s => s.CueTrackNumber).ToListAsync();
        rows.Should().HaveCount(3);
        rows.Select(s => s.CueTrackNumber).Should().Equal(1, 2, 3);
        rows.Select(s => s.StartOffsetTicks).Should().Equal(offsets.Select(offset => TimeSpan.FromSeconds(offset).Ticks));
        rows.Select(s => s.CueFilePath).Should().OnlyContain(cue => cue != null);
        rows[0].Id.Should().Be(originalId);
        (await context.PlaylistSongs.CountAsync(p => p.PlaylistId == playlistId && p.SongId == originalId)).Should().Be(1);
        return rows.Select(s => s.Id).ToList();
    }

    private async Task<List<Guid>> ReadSongIdsAsync(string path)
    {
        await using var context = _dbHelper.ContextFactory.CreateDbContext();
        return await context.Songs.Where(s => s.FilePath == path).OrderBy(s => s.CueTrackNumber).Select(s => s.Id).ToListAsync();
    }

    private static SongFileMetadata CreateFileMetadata(string audioPath, string cuePath, DateTime cueTime, DateTime audioTime,
        params (int Number, string Title, double Offset, double Duration)[] tracks)
    {
        return new SongFileMetadata
        {
            FilePath = audioPath,
            FileModifiedDate = audioTime,
            CueFilePath = cuePath,
            CueFileModifiedDate = cueTime,
            CueTracks = tracks.Select(t => new SongFileMetadata
            {
                FilePath = audioPath,
                FileModifiedDate = audioTime,
                CueTrackNumber = t.Number,
                TrackNumber = t.Number,
                StartOffsetTicks = TimeSpan.FromSeconds(t.Offset).Ticks,
                Duration = TimeSpan.FromSeconds(t.Duration),
                Title = t.Title,
                Artists = ["Artist"],
                AlbumArtists = ["Artist"],
                Album = "Album",
                CueFilePath = cuePath,
                CueFileModifiedDate = cueTime
            }).ToList()
        };
    }
}
