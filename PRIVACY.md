# Privacy Policy for Nagi Portable

Nagi Portable is a fork of [Nagi](https://github.com/Anthonyy232/Nagi), a local music player for Windows. This document explains what data the fork keeps and which online services it can contact.

## Local-First

Your music library, playlists, listening history, settings, caches and logs are stored only on your computer, in the `datadir` folder next to `Nagi.exe`. Nagi Portable has no accounts, no analytics and no telemetry, and it does not send your data to the fork's author.

## Online Features (Opt-In)

All online features are turned off by default. When you turn one on in Settings, Nagi sends only the track details needed for the request, such as artist name, track title, album title or duration, to the following services:

- **MusicBrainz** (online metadata): looks up artists. Only the artist's MusicBrainz ID is stored.
- **LRCLIB** and **NetEase Music** (online lyrics): look up synced or plain lyrics for the playing track.
- **Lyrics romanization packs**: when you browse or install packs in Settings, Nagi downloads the pack list and the pack files from the original Nagi project's releases on GitHub.

These requests identify themselves as Nagi in the User-Agent header. Like any internet request, they reveal your IP address to the service you contact.

## Removed From This Fork

The original Nagi can connect to Last.fm (including scrobbling), ListenBrainz, Discord Rich Presence, TheAudioDB, Fanart.tv and Spotify, and it checks GitHub for updates. Nagi Portable contains none of these integrations and does not check for updates.

---
*Last Updated: November 16, 2026*
