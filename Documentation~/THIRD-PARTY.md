# Third-Party Notices

This package vendors the following libraries for Zeroconf (DNS-SD / mDNS) advertise and discovery. License texts are included under `Runtime/Zeroconf/Vendor/`.

| Component | Version | License | Upstream |
|-----------|---------|---------|----------|
| [net-mdns](https://github.com/richardschneider/net-mdns) | 0.27.0 | MIT | Richard Schneider |
| [net-dns](https://github.com/richardschneider/net-dns) | 2.0.1 | MIT | Richard Schneider |
| [Common.Logging](https://github.com/net-commons/common-logging) | 3.4.1 | Apache-2.0 | net-commons |
| [SimpleBase](https://github.com/ssg/SimpleBase) | 2.1.0 | Apache-2.0 | Sedat Kapanoglu / ssg |

The vendor tree matches Unity-MIDI-Plugin’s `UdpMidi2Discovery` pin (same versions used for Network MIDI 2.0 discovery). SimpleBase may include compile fixes for older Unity runtimes from that tree.

Desktop .NET builds compile these sources via `Runtime/Runtime.csproj`. Unity uses `jp.kshoji.rtpmidi.zeroconf.vendor` (`Runtime/Zeroconf/Vendor/jp.kshoji.rtpmidi.zeroconf.vendor.asmdef`).
