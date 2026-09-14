# RTP-MIDI-for-.NET
RTP MIDI implementation for .NET

This is a porting of [Arduino-AppleMIDI-Library](https://github.com/lathoub/Arduino-AppleMIDI-Library) for C# applications.<br/>
Same as original, this implementation doesn't support RTP MIDI Journaling protocol.

## Third-party (Zeroconf / mDNS)

DNS-SD advertise and discovery use vendored [Makaretu.Dns](https://github.com/richardschneider/net-mdns) sources (same pin as Unity-MIDI-Plugin Network MIDI 2.0 discovery):

- net-mdns 0.27.0 (MIT)
- net-dns 2.0.1 (MIT)
- Common.Logging 3.4.1 (Apache-2.0)
- SimpleBase 2.1.0 (Apache-2.0)

Sources live under `Runtime/Zeroconf/Vendor/`. Full notices: [Documentation~/THIRD-PARTY.md](Documentation~/THIRD-PARTY.md).

Phase 0 smoke test: [Tools/ZeroconfAdvertisePrototype](Tools/ZeroconfAdvertisePrototype/README.md) (`_apple-midi._udp`, empty TXT).
