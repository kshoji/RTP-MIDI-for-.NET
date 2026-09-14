# Zeroconf Advertise Prototype (Phase 2)

Console smoke test for `MakaretuZeroconf` / `RtpMidiServer` advertise path: `_apple-midi._udp` with an **empty TXT**.

## Run

```bash
dotnet run --project Tools/ZeroconfAdvertisePrototype -- "My session name" 5004
```

Leave it running, then check:

- macOS: Audio MIDI Setup → MIDI Network Setup → Directory
- Windows: Tobias Erichsen rtpMIDI Directory (Bonjour required on the peer)
- CLI: `dns-sd -B _apple-midi._udp` / `dns-sd -L "My session name" _apple-midi._udp`

Press Enter to withdraw the advertisement and exit.

## Intentional differences from Network MIDI 2.0

| Item | MIDI 2.0 (`UdpMidi2Discovery` usage) | This prototype |
|------|--------------------------------------|----------------|
| Service type | `_midi2._udp` | `_apple-midi._udp` |
| TXT | `UMPEndpointName`, `ProductInstanceId` | empty (no keys) |
| Port | MIDI 2.0 UDP port | AppleMIDI **control** port only |
