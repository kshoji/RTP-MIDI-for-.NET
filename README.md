# RTP-MIDI-for-.NET
RTP MIDI implementation for .NET

This is a porting of [Arduino-AppleMIDI-Library](https://github.com/lathoub/Arduino-AppleMIDI-Library) for C# applications.<br/>
Same as original, this implementation doesn't support RTP MIDI Journaling protocol.

## Features

- AppleMIDI session control (`IN` / `OK` / `NO` / `BY` / `CK` / `RS`) and RTP-MIDI transport
- Optional Zeroconf advertise / browse for `_apple-midi._udp` (Makaretu.Dns / net-mdns)
- Manual `ConnectToListener(IPEndPoint)` remains available for non-Bonjour devices

## Quick start (Zeroconf)

```cs
var server = new RtpMidiServer("My session name", 5004, connectionListener, eventHandler);
server.Start(); // listens and advertises on the control port

server.StartDiscovery(discoveryListener);
// In OnServiceAppeared: server.ConnectToListener(service.ControlEndPoint);
```

See [Documentation~/jp.kshoji.rtpmidi.md](Documentation~/jp.kshoji.rtpmidi.md).

## Third-party (Zeroconf / mDNS)

DNS-SD advertise and discovery use vendored [Makaretu.Dns](https://github.com/richardschneider/net-mdns) sources (same pin as Unity-MIDI-Plugin Network MIDI 2.0 discovery):

- net-mdns 0.27.0 (MIT)
- net-dns 2.0.1 (MIT)
- Common.Logging 3.4.1 (Apache-2.0)
- SimpleBase 2.1.0 (Apache-2.0)

Sources live under `Runtime/Zeroconf/Vendor/`. Full notices: [Documentation~/THIRD-PARTY.md](Documentation~/THIRD-PARTY.md).

Advertise smoke test: [Tools/ZeroconfAdvertisePrototype](Tools/ZeroconfAdvertisePrototype/README.md).
