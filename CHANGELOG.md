# Changelog
All notable changes to this package will be documented in this file.

## [Unreleased]

### Added

* Zeroconf (`_apple-midi._udp`) advertise / browse via Makaretu.Dns:
  * `RtpMidiDnsSdConstants`, `RtpMidiDiscoveredService`, `IRtpMidiServiceDiscoveryListener`, `IRtpMidiZeroconf`, `FakeRtpMidiZeroconf`, `MakaretuZeroconf`, `RtpMidiZeroconfHelpers`
  * `RtpMidiServer` advertise on `Start` / withdraw on `Stop`, plus `StartDiscovery` / `StopDiscovery`
  * Empty TXT, IPv4 preferred, self-exclusion, FQDN-keyed browse results, NIC re-advertise, `ZeroconfException` notification
  * AppleMIDI invitation `SessionName` (UTF-8 NUL-terminated on IN/OK)
* Vendored Makaretu.Dns stack (net-mdns 0.27.0, net-dns 2.0.1, Common.Logging 3.4.1, SimpleBase 2.1.0) under `Runtime/Zeroconf/Vendor/`
* Unit tests in `Tests/RtpMidi.Zeroconf.Tests`
* Third-party notices in `Documentation~/THIRD-PARTY.md` and README
* `Tools/ZeroconfAdvertisePrototype` and `Tools/ZeroconfSessionSample`
* Interop checklist in `Documentation~/Zeroconf-Interop-Checklist.md`

## [1.0.0] - 2022-05-08

### Initial release

* Initial release.
* Ported RTP MIDI features from [Arduino-AppleMIDI-Library](https://github.com/lathoub/Arduino-AppleMIDI-Library), to C#.
* No dependency with Unity, so it also runs on pure .NET environment.
