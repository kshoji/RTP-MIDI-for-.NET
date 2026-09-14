# Zeroconf interoperability checklist

Manual checks for RTP-MIDI `_apple-midi._udp` advertise / browse / connect.
Automated coverage lives in `Tests/RtpMidi.Zeroconf.Tests` (fake + profile helpers).

## Protocol / packets

| Check | How | Pass? |
|-------|-----|-------|
| Service type `_apple-midi._udp` | `dns-sd -B _apple-midi._udp` / Wireshark | |
| SRV port = control only (not data N+1) | `dns-sd -L "<name>" _apple-midi._udp` | |
| TXT empty (no keys; not MIDI 2.0 TXT) | Resolve / packet capture | |
| Goodbye on `Stop()` | Directory entry disappears after stop | |

## Interop matrix

| Peer | Advertise → peer Directory | Peer → our browse | IN/OK connect |
|------|----------------------------|-------------------|---------------|
| macOS Audio MIDI Setup | | | |
| Tobias rtpMIDI (Windows + Bonjour) | | | |
| rtpmidid (recommended) | | | |
| KissBox Bonjour (optional) | | | |
| Manual IP only | N/A | N/A | `ConnectToListener` |

## Regression / edge

| Check | Notes | Pass? |
|-------|-------|-------|
| `advertiseOnStart: false` + manual connect | UDP session still works | |
| Self advertisement not auto-connected | Sample prints only; no auto `ConnectToListener` | |
| Duplicate Directory names | Two hosts with same instance name; both appear with distinct endpoints | |
| Firewall opens N but not N+1 | Existing invite timeout path | |
| IPv4 preferred over AAAA | Helpers + browse pipeline | |
| Advertise / browse failure | `ZeroconfException` notified; listen continues | |
| NIC change while advertising | Re-advertise refreshes addresses | |

## Suggested local commands

```bash
# Advertise + browse sample
dotnet run --project Tools/ZeroconfSessionSample -- "RTP-MIDI Sample" 5004

# Advertise-only smoke test
dotnet run --project Tools/ZeroconfAdvertisePrototype -- "RTP-MIDI Sample" 5004

# Browse / resolve (Bonjour dns-sd)
dns-sd -B _apple-midi._udp local.
dns-sd -L "RTP-MIDI Sample" _apple-midi._udp local.
```
