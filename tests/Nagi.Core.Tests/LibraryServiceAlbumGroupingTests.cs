using FluentAssertions;
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

public sealed class LibraryServiceAlbumGroupingTests : IDisposable
{
    private readonly DbContextFactoryTestHelper _dbHelper = new();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ProviderPipelineProvider _pipelines = TestProviderPipeline.Build(ServiceProviderIds.ImageDownload);
    private readonly LibraryService _libraryService;

    public LibraryServiceAlbumGroupingTests()
    {
        _settings.GetIgnoreLeadingArticlesOnSortEnabledAsync().Returns(Task.FromResult(false));
        _libraryService = new LibraryService(
            _dbHelper.ContextFactory,
            Substitute.For<IFileSystemService>(),
            Substitute.For<IMetadataService>(),
            Substitute.For<IMusicBrainzService>(),
            Substitute.For<IHttpClientFactory>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IPathConfiguration>(),
            _settings,
            Substitute.For<IReplayGainService>(),
            Substitute.For<IImageProcessor>(),
            _pipelines,
            Substitute.For<ILogger<LibraryService>>());
    }

    public void Dispose()
    {
        _libraryService.Dispose();
        _pipelines.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _dbHelper.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecursiveAlbumGrouping_SeparatesSameAlbumAcrossEditionDirectories(bool ignoreLeadingArticles)
    {
        _settings.IgnoreLeadingArticlesOnSortEnabledChanged += Raise.Event<Action<bool>>(ignoreLeadingArticles);

        var rootId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var root = new Folder { Id = rootId, Name = "Music", Path = @"C:\Music" };
        var parent = new Folder { Id = Guid.NewGuid(), Name = "Artist", Path = @"C:\Music\Artist", ParentFolderId = rootId };
        var editionA = new Folder { Id = Guid.NewGuid(), Name = "A", Path = @"C:\Music\Artist\A", ParentFolderId = parent.Id };
        var editionB = new Folder { Id = Guid.NewGuid(), Name = "B", Path = @"C:\Music\Artist\B", ParentFolderId = parent.Id };
        var excluded = new Folder { Id = Guid.NewGuid(), Name = "Elsewhere", Path = @"C:\Music\ArtistElsewhere", ParentFolderId = rootId };
        var otherRoot = new Folder { Id = Guid.NewGuid(), Name = "Other root", Path = @"C:\OtherMusic" };
        var album = new Album { Id = albumId, Title = "The Album", SortTitle = "Album", PrimaryArtistName = "Artist", PrimaryArtistSortName = "Artist" };

        var expected = new List<Guid>();
        var songs = new List<Song>();
        for (var track = 1; track <= 3; track++)
        {
            var id = Guid.NewGuid();
            expected.Add(id);
            songs.Add(new Song
            {
                Id = id, FolderId = rootId, AlbumId = albumId, Album = album,
                DirectoryPath = editionA.Path, FilePath = Path.Combine(editionA.Path, "album.flac"),
                CueTrackNumber = track, CueFilePath = Path.Combine(editionA.Path, "album.cue"),
                StartOffsetTicks = TimeSpan.FromSeconds(track * 10 - 10).Ticks,
                DurationTicks = TimeSpan.FromSeconds(10).Ticks, Title = $"Track{track}", SortTitle = $"Track{track}",
                TrackNumber = track, DiscNumber = 1, ArtistName = "Artist", PrimaryArtistName = "Artist", PrimaryArtistSortName = "Artist"
            });
        }
        for (var track = 1; track <= 3; track++)
        {
            var id = Guid.NewGuid();
            expected.Add(id);
            songs.Add(new Song
            {
                Id = id, FolderId = rootId, AlbumId = albumId, Album = album,
                DirectoryPath = editionB.Path, FilePath = Path.Combine(editionB.Path, $"Track{track}.flac"),
                Title = $"Track{track}", SortTitle = $"Track{track}", TrackNumber = track, DiscNumber = 1,
                ArtistName = "Artist", PrimaryArtistName = "Artist", PrimaryArtistSortName = "Artist"
            });
        }
        songs.Add(new Song { Id = Guid.NewGuid(), FolderId = rootId, AlbumId = albumId, Album = album, DirectoryPath = excluded.Path, FilePath = Path.Combine(excluded.Path, "outside.flac"), Title = "Outside", SortTitle = "Outside", TrackNumber = 1 });
        songs.Add(new Song { Id = Guid.NewGuid(), FolderId = otherRoot.Id, AlbumId = albumId, Album = album, DirectoryPath = Path.Combine(parent.Path, "OtherRoot"), FilePath = @"C:\OtherMusic\inside-artist-path.flac", Title = "Other root", SortTitle = "Other root", TrackNumber = 1 });

        await using (var context = _dbHelper.ContextFactory.CreateDbContext())
        {
            context.Folders.AddRange(root, parent, editionA, editionB, excluded, otherRoot);
            context.Albums.Add(album);
            context.Songs.AddRange(songs);
            await context.SaveChangesAsync();
        }

        var page = await _libraryService.GetSongsInDirectoryRecursiveOffsetAsync(rootId, parent.Path, 0, 200, SongSortOrder.AlbumGroupedAsc);
        page.TotalCount.Should().Be(6);
        page.Items.Select(s => s.Id).Should().Equal(expected);
        page.Items.Select(s => s.Id).Distinct().Should().HaveCount(6);
        page.Items.Take(3).Should().OnlyContain(s => s.DirectoryPath == editionA.Path);
        page.Items.Skip(3).Should().OnlyContain(s => s.DirectoryPath == editionB.Path);
        page.Items.Take(3).Select(s => s.CueTrackNumber).Should().Equal(1, 2, 3);
        page.Items.Take(3).Select(s => s.CueFilePath).Should().OnlyContain(p => p == Path.Combine(editionA.Path, "album.cue"));
        page.Items.Take(3).Select(s => s.StartOffsetTicks).Should().Equal(0, TimeSpan.FromSeconds(10).Ticks, TimeSpan.FromSeconds(20).Ticks);
        page.Items.Take(3).Select(s => s.DurationTicks).Should().OnlyContain(duration => duration == TimeSpan.FromSeconds(10).Ticks);
        page.Items.Skip(3).Select(s => s.CueTrackNumber).Should().OnlyContain(trackNumber => trackNumber == 0);
        page.Items.Take(3).Select(s => s.FilePath).Distinct().Should().ContainSingle();

        (await _libraryService.GetAllSongIdsInDirectoryRecursiveAsync(rootId, parent.Path, SongSortOrder.AlbumGroupedAsc)).Should().Equal(expected);
        var paged = (await _libraryService.GetSongsInDirectoryRecursiveOffsetAsync(rootId, parent.Path, 0, 2, SongSortOrder.AlbumGroupedAsc)).Items
            .Concat((await _libraryService.GetSongsInDirectoryRecursiveOffsetAsync(rootId, parent.Path, 2, 2, SongSortOrder.AlbumGroupedAsc)).Items)
            .Concat((await _libraryService.GetSongsInDirectoryRecursiveOffsetAsync(rootId, parent.Path, 4, 2, SongSortOrder.AlbumGroupedAsc)).Items).Select(s => s.Id);
        paged.Should().Equal(expected);

        var child = await _libraryService.GetSongsInDirectoryRecursiveOffsetAsync(rootId, editionA.Path, 0, 200, SongSortOrder.AlbumGroupedAsc);
        child.TotalCount.Should().Be(3);
        child.Items.Select(s => s.Id).Should().Equal(expected.Take(3));
    }

}
