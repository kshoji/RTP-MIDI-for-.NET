# Zeroconf vendor dependencies (Makaretu.Dns)

Vendored mDNS / DNS-SD sources for RTP-MIDI Zeroconf advertise and browse.

Copied from Unity-MIDI-Plugin `Assets/MIDI/Scripts/UdpMidi2Discovery/` (Network MIDI 2.0 discovery stack).

| Component | Version | Upstream | License |
|-----------|---------|----------|---------|
| net-mdns | 0.27.0 | https://github.com/richardschneider/net-mdns | MIT |
| net-dns | 2.0.1 | https://github.com/richardschneider/net-dns | MIT |
| Common.Logging | 3.4.1 | https://github.com/net-commons/common-logging | Apache-2.0 |
| SimpleBase | 2.1.0 | https://github.com/ssg/SimpleBase | Apache-2.0 |

SimpleBase may include Unity-oriented compile fixes from the Unity-MIDI-Plugin vendor tree.

**RTP-MIDI note:** advertise `_apple-midi._udp` with an empty TXT (no keys). Do not reuse Network MIDI 2.0 TXT keys (`UMPEndpointName`, `ProductInstanceId`). `ServiceProfile` defaults to `txtvers=1`; clear that before advertising.
