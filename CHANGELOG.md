# Changelog
All notable changes to this package will be documented in this file.

## [Unreleased]

### Added

* Vendored Makaretu.Dns stack (net-mdns 0.27.0, net-dns 2.0.1, Common.Logging 3.4.1, SimpleBase 2.1.0) under `Runtime/Zeroconf/Vendor/` for upcoming `_apple-midi._udp` Zeroconf support (Phase 0).
* Third-party notices in `Documentation~/THIRD-PARTY.md` and README.
* `Tools/ZeroconfAdvertisePrototype` console smoke test (empty TXT, control port only).

## [1.0.0] - 2022-05-08

### Initial release

* Initial release.
* Ported RTP MIDI features from [Arduino-AppleMIDI-Library](https://github.com/lathoub/Arduino-AppleMIDI-Library), to C#.
* No dependency with Unity, so it also runs on pure .NET environment.
