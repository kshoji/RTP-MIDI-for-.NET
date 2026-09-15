# RTP-MIDI-for-.NET
RTP MIDI implementation for .NET

This is a port of [Arduino-AppleMIDI-Library](https://github.com/lathoub/Arduino-AppleMIDI-Library) for C# applications.

The library implements [RFC 6295](https://datatracker.ietf.org/doc/html/rfc6295) Recovery Journal with **default semantics** and a **closed-loop** checkpoint, using AppleMIDI Recovery Journal Setting (`RS`) as the receiver report. Enhanced Chapter C (`H=1`) and SDP session-description relaxations (`j_update`, `ch_never`) are **not** supported.

Usage: [Documentation~/jp.kshoji.rtpmidi.md](Documentation~/jp.kshoji.rtpmidi.md)
