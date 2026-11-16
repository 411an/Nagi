using System.Globalization;
using FluentAssertions;
using Nagi.Core.Helpers;
using Nagi.Core.Models;
using Xunit;

namespace Nagi.Core.Tests;

public class CuePlaybackTests
{
    [Fact]
    public void ToSeekPosition_CueTrack_ClampsLocalPosition()
    {
        var song = CreateCueTrack();

        CuePlayback.ToSeekPosition(song, TimeSpan.FromMilliseconds(12500))
            .Should().Be(TimeSpan.FromMilliseconds(12500));
        CuePlayback.ToSeekPosition(song, TimeSpan.FromSeconds(-1))
            .Should().Be(TimeSpan.Zero);
        CuePlayback.ToSeekPosition(song, TimeSpan.FromSeconds(300))
            .Should().Be(TimeSpan.FromMilliseconds(264240));
    }

    [Fact]
    public void ToTrackPosition_CueTrack_ClampsLocalPosition()
    {
        var song = CreateCueTrack();

        CuePlayback.ToTrackPosition(song, TimeSpan.FromMilliseconds(12500))
            .Should().Be(TimeSpan.FromMilliseconds(12500));
        CuePlayback.ToTrackPosition(song, TimeSpan.FromSeconds(-1))
            .Should().Be(TimeSpan.Zero);
        CuePlayback.ToTrackPosition(song, TimeSpan.FromSeconds(700))
            .Should().Be(TimeSpan.FromMilliseconds(264240));
    }

    [Fact]
    public void GetMediaOptions_CueTrack_UsesInvariantDecimalFormatting()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            CuePlayback.GetMediaOptions(CreateCueTrack())
                .Should().Equal(":start-time=341.44", ":stop-time=605.68");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void NormalSong_HasNoCueOptionsAndOnlyClampsNegativePositions()
    {
        var song = new Song
        {
            CueTrackNumber = 0,
            Duration = TimeSpan.FromSeconds(10)
        };

        CuePlayback.GetMediaOptions(song).Should().BeEmpty();
        CuePlayback.ToSeekPosition(song, TimeSpan.FromSeconds(20))
            .Should().Be(TimeSpan.FromSeconds(20));
        CuePlayback.ToTrackPosition(song, TimeSpan.FromSeconds(20))
            .Should().Be(TimeSpan.FromSeconds(20));
        CuePlayback.ToSeekPosition(song, TimeSpan.FromSeconds(-1))
            .Should().Be(TimeSpan.Zero);
        CuePlayback.ToTrackPosition(song, TimeSpan.FromSeconds(-1))
            .Should().Be(TimeSpan.Zero);
    }

    private static Song CreateCueTrack() => new()
    {
        CueTrackNumber = 2,
        StartOffsetTicks = TimeSpan.FromMilliseconds(341440).Ticks,
        Duration = TimeSpan.FromMilliseconds(264240)
    };
}
