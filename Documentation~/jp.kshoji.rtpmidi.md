# How to use this library

## Third-party (Zeroconf / mDNS)

mDNS / DNS-SD dependencies are vendored under `Runtime/Zeroconf/Vendor/` (Makaretu.Dns). See [THIRD-PARTY.md](THIRD-PARTY.md) for licenses and versions.

Optional smoke test: [Tools/ZeroconfAdvertisePrototype](../Tools/ZeroconfAdvertisePrototype/README.md).  
Session sample: [Tools/ZeroconfSessionSample](../Tools/ZeroconfSessionSample/README.md).  
Interop checklist: [Zeroconf-Interop-Checklist.md](Zeroconf-Interop-Checklist.md).

## Use this library with Unity
At first, setup the library to Unity Project.
Open the `manifest.json` for your project and add the following entry to your list of dependencies.

```json
"jp.kshoji.rtpmidi": "https://github.com/kshoji/RTP-MIDI-for-.NET.git",
```

## Start RTP MIDI Listener
`Listener` can accept connection from the other RTP MIDI `Initiator` host.
```cs
class RtpMidiDeviceConnectionListenerImpl : IRtpMidiDeviceConnectionListener
{
    public void OnRtpMidiDeviceAttached(string deviceId)
    {
        Console.WriteLine($"device {deviceId} connected.");
    }

    public void OnRtpMidiDeviceDetached(string deviceId)
    {
        Console.WriteLine($"device {deviceId} disconnected.");
    }
}

var connectionListener = new RtpMidiDeviceConnectionListenerImpl();
var midiEventHandler = new MidiEventHandler(); // see Receive MIDI events

// Control port 5004, data port 5005. Start() also advertises _apple-midi._udp by default.
var rtpMidiServer = new RtpMidiServer("My session name", 5004, connectionListener, midiEventHandler);
rtpMidiServer.Start();
```

To disable Bonjour/Zeroconf advertise while keeping UDP listen:

```cs
var rtpMidiServer = new RtpMidiServer(
    "My session name", 5004, connectionListener, midiEventHandler,
    zeroconf: null, advertiseOnStart: false);
rtpMidiServer.Start();
```

## Discover remote sessions (Zeroconf)
Browse LAN Directory entries (`_apple-midi._udp`), then connect with the resolved control endpoint.

```cs
class DiscoveryListener : IRtpMidiServiceDiscoveryListener
{
    private readonly RtpMidiServer server;

    public DiscoveryListener(RtpMidiServer server) => this.server = server;

    public void OnServiceAppeared(RtpMidiDiscoveredService service)
    {
        Console.WriteLine($"Found {service.ServiceName} at {service.ControlEndPoint}");
        server.ConnectToListener(service.ControlEndPoint);
    }

    public void OnServiceDisappeared(string serviceName)
    {
        Console.WriteLine($"Lost {serviceName}");
    }
}

rtpMidiServer.StartDiscovery(new DiscoveryListener(rtpMidiServer));
// ...
rtpMidiServer.StopDiscovery();
```

## Start RTP MIDI Initiator (manual IP)
`Initiator` can connect without Zeroconf by specifying IP + control port.
```cs
rtpMidiServer.ConnectToListener(new IPEndPoint(IPAddress.Parse("192.168.0.100"), 5004));
```

To close that session later (sends peer-clear when journaling is enabled, then AppleMIDI `BY`, and raises detach):
```cs
rtpMidiServer.DisconnectFromListener(new IPEndPoint(IPAddress.Parse("192.168.0.100"), 5004));
```

## Receive MIDI events
```cs
class MidiEventHandler : IRtpMidiEventHandler
{
    public void OnMidiNoteOn(string deviceId, int channel, int note, int velocity)
    {
        Console.WriteLine($"Note on channel: {channel}, note: {note}, velocity: {velocity}");
    }
    // Implement the remaining IRtpMidiEventHandler members...
}
```

Pass the handler to the `RtpMidiServer` constructor (see Listener example above).

## Send MIDI events
```cs
// deviceId comes from IRtpMidiDeviceConnectionListener.OnRtpMidiDeviceAttached.
rtpMidiServer.SendMidiNoteOn(deviceId, 0, 64, 127);
```

## Stop
```cs
rtpMidiServer.StopDiscovery();
rtpMidiServer.Stop(); // withdraws Zeroconf advertise and stops UDP session
```

## Recovery Journal

MIDI packets include an RFC 6295 Recovery Journal by default: **default chapter semantics**, a **closed-loop** checkpoint, and AppleMIDI Recovery Journal Setting (`RS`) as the receiver report. Packets that carry a journal set `J=1`.

The journal exists so lost packets do not leave stuck notes or controllers. The receiver repairs from the difference between the journal and MIDI it has already applied. On a locally initiated disconnect, the library sends All Sound Off, Reset All Controllers, and All Notes Off on every channel before AppleMIDI `BY` when the data port can still send.

### Disable

- SDK / `dotnet build`: pass `-p:EnableRtpMidiJournal=false`.
- Unity: remove `-define:ENABLE_RTP_MIDI_JOURNAL` from `Runtime/csc.rsp` (and `Runtime/mcs.rsp` on Unity 2018.4). Those files turn the feature on for the package assembly.

### Not supported

- Enhanced Chapter C (`H=1`). Chapter C is encoded with default semantics (`H=0`).
- SDP session-description relaxations such as `j_update` and `ch_never` (open-loop / anchor send policies). Session control is AppleMIDI (`IN` / `OK` / `BY` / `CK` / `RS`) only.

### Sample

`Samples~/JournalLoopback` is a localhost AppleMIDI loopback Unity project. It checks handshake, live MIDI with journals, repair after a dropped packet, trailing-journal recovery, and disconnect cleanup.
