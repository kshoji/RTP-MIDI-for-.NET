using jp.kshoji.rtpmidi;

namespace jp.kshoji.rtpmidi.tools;

/// <summary>
/// Console sample: advertise + browse via <see cref="RtpMidiServer"/>.
/// Does not auto-connect; prints discovered control endpoints for manual verification.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var sessionName = args.Length > 0 ? args[0] : "RTP-MIDI Zeroconf Sample";
        var controlPort = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 5004;

        Console.WriteLine($"Session \"{sessionName}\" control={controlPort} data={controlPort + 1}");
        Console.WriteLine("Advertising and browsing _apple-midi._udp. Press Enter to stop.");
        Console.WriteLine("Discovery never auto-connects — use ConnectToListener with a printed endpoint.");

        var server = new RtpMidiServer(
            sessionName,
            controlPort,
            new NullConnectionListener(),
            new NullEventHandler());
        server.SetRtpMidiExceptionListener(new ConsoleExceptionListener());

        var discovery = new ConsoleDiscoveryListener();
        try
        {
            server.Start();
            server.StartDiscovery(discovery);
            Console.ReadLine();
        }
        finally
        {
            server.StopDiscovery();
            server.Stop();
        }

        return 0;
    }

    private sealed class ConsoleDiscoveryListener : IRtpMidiServiceDiscoveryListener
    {
        public void OnServiceAppeared(RtpMidiDiscoveredService service)
        {
            Console.WriteLine($"[+] {service.ServiceName} @ {service.ControlEndPoint} ({service.HostName})");
        }

        public void OnServiceDisappeared(string serviceName)
        {
            Console.WriteLine($"[-] {serviceName}");
        }
    }

    private sealed class ConsoleExceptionListener : IRtpMidiExceptionListener
    {
        public void OnError(RtpMidiExceptionKind exceptionKind)
        {
            Console.WriteLine($"[!] {exceptionKind}");
        }
    }

    private sealed class NullConnectionListener : IRtpMidiDeviceConnectionListener
    {
        public void OnRtpMidiDeviceAttached(string deviceId)
        {
        }

        public void OnRtpMidiDeviceDetached(string deviceId)
        {
        }
    }

    private sealed class NullEventHandler : IRtpMidiEventHandler
    {
        public void OnMidiNoteOn(string deviceId, int channel, int note, int velocity) { }
        public void OnMidiNoteOff(string deviceId, int channel, int note, int velocity) { }
        public void OnMidiPolyphonicAftertouch(string deviceId, int channel, int note, int pressure) { }
        public void OnMidiControlChange(string deviceId, int channel, int function, int value) { }
        public void OnMidiProgramChange(string deviceId, int channel, int program) { }
        public void OnMidiChannelAftertouch(string deviceId, int channel, int pressure) { }
        public void OnMidiPitchWheel(string deviceId, int channel, int amount) { }
        public void OnMidiSystemExclusive(string deviceId, byte[] systemExclusive) { }
        public void OnMidiTimeCodeQuarterFrame(string deviceId, int timing) { }
        public void OnMidiSongSelect(string deviceId, int song) { }
        public void OnMidiSongPositionPointer(string deviceId, int position) { }
        public void OnMidiTuneRequest(string deviceId) { }
        public void OnMidiTimingClock(string deviceId) { }
        public void OnMidiStart(string deviceId) { }
        public void OnMidiContinue(string deviceId) { }
        public void OnMidiStop(string deviceId) { }
        public void OnMidiActiveSensing(string deviceId) { }
        public void OnMidiReset(string deviceId) { }
    }
}
