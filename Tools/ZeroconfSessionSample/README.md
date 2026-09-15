# Zeroconf Session Sample

Console sample that uses `RtpMidiServer` to:

1. Advertise `_apple-midi._udp` (empty TXT, control port only)
2. Browse / resolve peers
3. Print discoveries (no auto-connect)

## Run

```bash
dotnet run --project Tools/ZeroconfSessionSample -- "My session name" 5004
```

Verify with `dns-sd -B _apple-midi._udp` or macOS Audio MIDI Setup / Tobias rtpMIDI Directory.

Press Enter to withdraw and exit.

## Notes

- Discovery does **not** call `ConnectToListener` automatically (avoids accidental self/loop connections).
- Zeroconf failures raise `RtpMidiExceptionKind.ZeroconfException` but do not stop UDP listen.
- Manual connect: `server.ConnectToListener(new IPEndPoint(...))`.
