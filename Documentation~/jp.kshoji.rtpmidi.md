# How to use this library

## Third-party (Zeroconf / mDNS)

mDNS / DNS-SD dependencies are vendored under `Runtime/Zeroconf/Vendor/` (Makaretu.Dns). See [THIRD-PARTY.md](THIRD-PARTY.md) for licenses and versions.

Optional smoke test: [Tools/ZeroconfAdvertisePrototype](../Tools/ZeroconfAdvertisePrototype/README.md).

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
