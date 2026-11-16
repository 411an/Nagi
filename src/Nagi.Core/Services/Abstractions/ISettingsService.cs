using Nagi.Core.Models;
using Nagi.Core.Services.Data;

namespace Nagi.Core.Services.Abstractions;

/// <summary>
///     Defines a service for managing application-wide, non-UI settings.
///     This interface is safe to use in the Core project.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    ///     Occurs when the volume normalization (ReplayGain) setting is changed.
    ///     The boolean parameter indicates whether volume normalization is enabled.
    /// </summary>
    event Action<bool>? VolumeNormalizationEnabledChanged;

    /// <summary>
    ///     Occurs when the fade on play/pause setting is changed.
    ///     The boolean parameter indicates whether fading is enabled.
    /// </summary>
    event Action<bool>? FadeOnPlayPauseEnabledChanged;

    /// <summary>
    ///     Occurs when the fade in duration is changed.
    /// </summary>
    event Action<int>? FadeInDurationChanged;

    /// <summary>
    ///     Occurs when the fade out duration is changed.
    /// </summary>
    event Action<int>? FadeOutDurationChanged;

    /// <summary>
    ///     Occurs when the fetch online metadata setting is changed.
    ///     The boolean parameter indicates whether fetching online metadata is enabled.
    /// </summary>
    event Action<bool>? FetchOnlineMetadataEnabledChanged;

    /// <summary>
    ///     Occurs when the fetch online lyrics setting is changed.
    ///     The boolean parameter indicates whether fetching online lyrics is enabled.
    /// </summary>
    event Action<bool>? FetchOnlineLyricsEnabledChanged;

    /// <summary>
    ///     Occurs when the romanized lyrics display setting is changed.
    /// </summary>
    event Action<bool>? LyricsRomanizationEnabledChanged;

    /// <summary>
    ///     Gets the initial volume level for the media player.
    /// </summary>
    /// <returns>A volume level between 0.0 and 1.0.</returns>
    Task<double> GetInitialVolumeAsync();

    /// <summary>
    ///     Saves the current volume level.
    /// </summary>
    /// <param name="volume">The volume level to save, clamped between 0.0 and 1.0.</param>
    Task SaveVolumeAsync(double volume);

    /// <summary>
    ///     Gets the initial mute state for the media player.
    /// </summary>
    /// <returns>True if the player should be muted; otherwise, false.</returns>
    Task<bool> GetInitialMuteStateAsync();

    /// <summary>
    ///     Saves the current mute state.
    /// </summary>
    /// <param name="isMuted">The mute state to save.</param>
    Task SaveMuteStateAsync(bool isMuted);

    /// <summary>
    ///     Gets the initial shuffle state for music playback.
    /// </summary>
    /// <returns>True if shuffle mode is enabled; otherwise, false.</returns>
    Task<bool> GetInitialShuffleStateAsync();

    /// <summary>
    ///     Saves the current shuffle state.
    /// </summary>
    /// <param name="isEnabled">The shuffle state to save.</param>
    Task SaveShuffleStateAsync(bool isEnabled);

    /// <summary>
    ///     Gets the initial repeat mode for music playback.
    /// </summary>
    /// <returns>The saved <see cref="RepeatMode" />.</returns>
    Task<RepeatMode> GetInitialRepeatModeAsync();

    /// <summary>
    ///     Saves the current repeat mode.
    /// </summary>
    /// <param name="mode">The repeat mode to save.</param>
    Task SaveRepeatModeAsync(RepeatMode mode);

    /// <summary>
    ///     Gets whether the application should restore playback state on launch.
    /// </summary>
    /// <returns>True if restoring playback state is enabled; otherwise, false.</returns>
    Task<bool> GetRestorePlaybackStateEnabledAsync();

    /// <summary>
    ///     Sets the restore playback state preference.
    /// </summary>
    /// <param name="isEnabled">The restore playback state preference to save.</param>
    Task SetRestorePlaybackStateEnabledAsync(bool isEnabled);

    /// <summary>
    ///     Gets whether the application should fetch additional metadata from online services.
    /// </summary>
    /// <returns>True if fetching online metadata is enabled; otherwise, false.</returns>
    Task<bool> GetFetchOnlineMetadataEnabledAsync();

    /// <summary>
    ///     Sets the preference for fetching additional metadata from online services.
    /// </summary>
    /// <param name="isEnabled">The preference to save.</param>
    Task SetFetchOnlineMetadataEnabledAsync(bool isEnabled);

    /// <summary>
    ///     Saves the current playback state, including the queue and current track.
    /// </summary>
    /// <param name="state">The playback state to save. If null, the saved state is cleared.</param>
    Task SavePlaybackStateAsync(PlaybackState? state);

    /// <summary>
    ///     Retrieves the last saved playback state.
    /// </summary>
    /// <returns>The saved <see cref="PlaybackState" />, or null if none exists.</returns>
    Task<PlaybackState?> GetPlaybackStateAsync();

    /// <summary>
    ///     Explicitly clears any saved playback state.
    /// </summary>
    Task ClearPlaybackStateAsync();

    /// <summary>
    ///     Gets whether fetching lyrics from online sources is enabled.
    /// </summary>
    Task<bool> GetFetchOnlineLyricsEnabledAsync();

    /// <summary>
    ///     Sets the preference for fetching lyrics from online sources.
    /// </summary>
    Task SetFetchOnlineLyricsEnabledAsync(bool isEnabled);

    /// <summary>
    ///     Gets whether romanized lyric display is enabled.
    /// </summary>
    Task<bool> GetLyricsRomanizationEnabledAsync();

    /// <summary>
    ///     Sets whether romanized lyric display is enabled.
    /// </summary>
    Task SetLyricsRomanizationEnabledAsync(bool isEnabled);

    /// <summary>
    ///     Gets the characters used to split multiple artists in a single string.
    /// </summary>
    /// <returns>A string containing the split characters.</returns>
    Task<string> GetArtistSplitCharactersAsync();

    /// <summary>
    ///     Sets the characters used to split multiple artists in a single string.
    /// </summary>
    /// <param name="characters">The characters to save.</param>
    Task SetArtistSplitCharactersAsync(string characters);

    /// <summary>
    ///     Occurs when the artist split characters setting has changed.
    /// </summary>
    event Action? ArtistSplitCharactersChanged;

    /// <summary>
    ///     Gets the characters used to split multiple genres in a single string.
    /// </summary>
    /// <returns>A string containing the split characters.</returns>
    Task<string> GetGenreSplitCharactersAsync();

    /// <summary>
    ///     Sets the characters used to split multiple genres in a single string.
    /// </summary>
    /// <param name="characters">The characters to save.</param>
    Task SetGenreSplitCharactersAsync(string characters);

    /// <summary>
    ///     Occurs when the genre split characters setting has changed.
    /// </summary>
    event Action? GenreSplitCharactersChanged;

    /// <summary>
    ///     Retrieves the last saved equalizer settings.
    /// </summary>
    /// <returns>The saved <see cref="EqualizerSettings" />, or null if none exist.</returns>
    Task<EqualizerSettings?> GetEqualizerSettingsAsync();

    /// <summary>
    ///     Saves the current equalizer settings.
    /// </summary>
    /// <param name="settings">The equalizer settings to save.</param>
    Task SetEqualizerSettingsAsync(EqualizerSettings settings);

    /// <summary>
    ///     Gets whether volume normalization (ReplayGain) is enabled.
    /// </summary>
    /// <returns>True if volume normalization is enabled; otherwise, false.</returns>
    Task<bool> GetVolumeNormalizationEnabledAsync();

    /// <summary>
    ///     Sets the volume normalization (ReplayGain) preference.
    /// </summary>
    /// <param name="isEnabled">The preference to save.</param>
    Task SetVolumeNormalizationEnabledAsync(bool isEnabled);

    /// <summary>Gets whether VLC should use WASAPI exclusive mode.</summary>
    Task<bool> GetWasapiExclusiveModeEnabledAsync();

    /// <summary>Sets whether VLC should use WASAPI exclusive mode.</summary>
    Task SetWasapiExclusiveModeEnabledAsync(bool isEnabled);

    /// <summary>
    ///     Gets whether fade on play/pause is enabled.
    /// </summary>
    Task<bool> GetFadeOnPlayPauseEnabledAsync();

    /// <summary>
    ///     Sets the fade on play/pause preference.
    /// </summary>
    Task SetFadeOnPlayPauseEnabledAsync(bool isEnabled);

    /// <summary>
    ///     Gets the fade in duration in milliseconds.
    /// </summary>
    Task<int> GetFadeInDurationMsAsync();

    /// <summary>
    ///     Sets the fade in duration in milliseconds.
    /// </summary>
    Task SetFadeInDurationMsAsync(int durationMs);

    /// <summary>
    ///     Gets the fade out duration in milliseconds.
    /// </summary>
    Task<int> GetFadeOutDurationMsAsync();

    /// <summary>
    ///     Sets the fade out duration in milliseconds.
    /// </summary>
    Task SetFadeOutDurationMsAsync(int durationMs);

    /// <summary>
    ///     Gets whether leading English articles ("the", "a", "an") should be ignored when
    ///     sorting albums, artists, and songs alphabetically.
    /// </summary>
    Task<bool> GetIgnoreLeadingArticlesOnSortEnabledAsync();

    /// <summary>
    ///     Sets the preference for ignoring leading articles during alphabetical sort.
    /// </summary>
    Task SetIgnoreLeadingArticlesOnSortEnabledAsync(bool isEnabled);

    /// <summary>
    ///     Occurs when the "ignore leading articles on sort" setting has changed.
    /// </summary>
    event Action<bool>? IgnoreLeadingArticlesOnSortEnabledChanged;

    /// <summary>
    ///     Resets all application-wide settings to their default values.
    /// </summary>
    Task ResetToDefaultsAsync();

    /// <summary>
    ///     Occurs when service provider configurations have changed.
    ///     The parameter indicates which category was modified.
    /// </summary>
    event Action<ServiceCategory>? ServiceProvidersChanged;

    /// <summary>
    ///     Gets the configured service providers for a specific category.
    /// </summary>
    /// <param name="category">The service category to retrieve.</param>
    /// <returns>A list of service provider settings, ordered by priority.</returns>
    Task<List<ServiceProviderSetting>> GetServiceProvidersAsync(ServiceCategory category);

    /// <summary>
    ///     Sets the configured service providers for a specific category.
    /// </summary>
    /// <param name="category">The service category to update.</param>
    /// <param name="providers">The list of service provider settings to save.</param>
    Task SetServiceProvidersAsync(ServiceCategory category, List<ServiceProviderSetting> providers);

    /// <summary>
    ///     Gets enabled service providers for a category, sorted by priority (lowest Order first).
    /// </summary>
    /// <param name="category">The service category to retrieve.</param>
    /// <returns>A list of enabled service providers, sorted by priority.</returns>
    Task<List<ServiceProviderSetting>> GetEnabledServiceProvidersAsync(ServiceCategory category);

    /// <summary>
    ///     Ensures all pending settings changes are written to persistent storage.
    /// </summary>
    Task FlushAsync();

    /// <summary>
    ///     Stops further writes to settings.json (used after restoring a backup, before restart).
    /// </summary>
    void FreezeWrites();

    /// <summary>
    ///     True after <see cref="FreezeWrites" />. Playback state must not be saved over a restore.
    /// </summary>
    bool AreWritesFrozen { get; }
}
