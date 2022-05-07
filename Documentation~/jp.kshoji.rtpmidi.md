# How to use this library

## Use this library with Unity
At first, setup the library to Unity Project.
Open the `manifest.json` for your project and add the following entry to your list of dependencies.

```json
"jp.kshoji.rtpmidi": "https://github.com/kshoji/RTP-MIDI-for-.NET.git",
```

## Start RTP MIDI Listener
`Listener` can accept connection from the other RTP MIDI `Initiator` host.
```cs
// defines and create a instance of RTP MIDI connection listener.
class RtpMidiDeviceConnectionListenerImpl : IRtpMidiDeviceConnectionListener
{
    void OnRtpMidiDeviceAttached(string deviceId)
    {
        Console.WriteLine($"device {deviceId} connected.");
    }

    void OnRtpMidiDeviceDetached(string deviceId)
    {
        Console.WriteLine($"device {deviceId} disconnected.");
    }
}
var connectionListner = new RtpMidiDeviceConnectionListenerImpl();

// Create a server instance, listening UDP port 5004 (control port), and 5005 (data port)
var rtpMidiServer = new RtpMidiServer("My session name", 5004, connectionListener);
// Start the server. Now, the server can connect from the RTP MIDI Initiator host.
rtpMidiServer.Start();
```

## Start RTP MIDI Initiator
`Initiator` can connect to the another `Listener` hosts.
```cs
// Connect to another RTP MIDI listener
rtpMidiServer.ConnectToListener(new IPEndpoint(IPAddress.Parse("192.168.0.100"), 5004));
```

## Receive MIDI events
```cs
// defines and create a instance of RTP MIDI event listener.
class MidiEventHandler : IRtpMidiEventHandler
{
    void OnMidiNoteOn(int channel, int note, int velocity)
    {
        Console.WriteLine($"Note on channel: {channel}, note: {note}, velocity: {velocity}");
    }
...
}
var midiEventHandler = new MidiEventHandler();

// attach the RTP MIDI event listener.
rtpMidiServer.SetMidiEventListener(midiEventHandler);
```

## Send MIDI events
```cs
// Example: send note on event with channel 1, note 64, velocity 127
// deviceId can obtain from RtpMidiDeviceConnectionListenerImpl.OnRtpMidiDeviceAttached callback.
rtpMidiServer.SendMidiNoteOn(deviceId, 0, 64, 127);
```
