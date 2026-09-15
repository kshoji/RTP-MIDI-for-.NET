# Journal Loopback

Unity project for Recovery Journal integration checks that unit tests do not cover. It does not inspect chapter byte layouts. On a localhost AppleMIDI session it checks:

- Handshake
- Note On/Off, SysEx, Quarter Frame, Start, and Timing Clock arrive with `J=1`
- Note Off / Volume / Program / Pitch Bend lost in one packet are repaired by a later journal
- Note Off recovers from a trailing journal with no extra MIDI
- Disconnecting with a note held delivers All Sound Off, Reset All Controllers, and All Notes Off to the Listener, plus a detach notification

Recovery journals are **on by default** in the library (`ENABLE_RTP_MIDI_JOURNAL` via `Runtime/csc.rsp`). This sample also adds the symbol in Player Settings so its own recovery scenarios compile.

## Run

1. Open `Samples~/JournalLoopback` in Unity 2019.4 or later (2021.3 recommended).
2. The first launch opens `Assets/Scenes/JournalLoopback.unity`. If it does not, use the menu `RTP-MIDI/Open Journal Loopback Sample`.
3. Enter Play Mode and click **Run all**.

Listener control port is 50104; Initiator is 50114. The handshake fails if those ports are already in use.
