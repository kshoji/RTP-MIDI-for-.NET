# Changelog
All notable changes to this package will be documented in this file.

## [1.1.0] - 2026-09-11

### Added

* RFC 6295 Recovery Journal (default semantics, closed-loop checkpoint, AppleMIDI `RS`).
* Channel chapters N, E, C (`H=0`), M, P, W, T, A and system chapters D, V, Q, F, X.
* Receiver delta repair, session-history chapters after prune, SysEx segments aligned with Chapter X, a 1200-octet UDP payload budget, and All Sound Off / Reset All Controllers / All Notes Off before a locally initiated `BY`.
* Unity loopback sample under `Samples~/JournalLoopback`.

Recovery journals are **on by default**. SDK builds can pass `-p:EnableRtpMidiJournal=false`. Enhanced Chapter C and SDP `j_update` / `ch_never` are not supported.

## [1.0.0] - 2022-05-08

### Initial release

* Initial release.
* Ported RTP MIDI features from [Arduino-AppleMIDI-Library](https://github.com/lathoub/Arduino-AppleMIDI-Library), to C#.
* No dependency with Unity, so it also runs on pure .NET environment.
