using Nagi.Core.Models;

namespace Nagi.Core.Services.Abstractions;

/// <summary>
///     Gives the lyrics of a song as plain text to read, without timing.
/// </summary>
public interface ILyricsTextService
{
    /// <summary>
    ///     The text of the song's lyrics file (see <see cref="ILrcService.FindLocalLyricsPath" />) or,
    ///     when there is none, of the lyrics tag in the audio file. Timestamps are dropped. Never goes
    ///     online. Reads the disk on the calling thread.
    /// </summary>
    /// <returns>The text, or <see langword="null" /> when the song has no lyrics.</returns>
    Task<string?> GetLyricsTextAsync(Song song, CancellationToken cancellationToken = default);
}
