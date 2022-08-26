using System.Net;
using System.Net.Sockets;
using jp.kshoji.rtpmidi;

namespace Example;

internal static class Program
{
    private static RtpMidiServer? server;

    private static bool isRunning = true;

    private static void Main(string[] args)
    {
        // Parse command line args
        var showHelp = false;
        var nextArgIsHost = false;
        var nextArgIsPort = false;
        var nextArgIsServerPort = false;
        var host = "";
        var port = 5004;
        var serverPort = 5004;
        foreach (var arg in args)
        {
            if (arg == "--help")
            {
                showHelp = true;
            }

            if (arg == "--host")
            {
                nextArgIsHost = true;
                continue;
            }
            if (nextArgIsHost)
            {
                host = arg;
                nextArgIsHost = false;
                continue;
            }

            if (arg == "--port")
            {
                nextArgIsPort = true;
                continue;
            }
            if (nextArgIsPort)
            {
                int.TryParse(arg, out port);
                nextArgIsPort = false;
                continue;
            }

            if (arg == "--serverPort")
            {
                nextArgIsServerPort = true;
                continue;
            }
            if (nextArgIsServerPort)
            {
                int.TryParse(arg, out serverPort);
                nextArgIsServerPort = false;
                continue;
            }
        }

        // Show the usage and exit
        if (showHelp)
        {
            var exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName;
            Console.WriteLine("Usage");
            Console.WriteLine("=====");
            Console.WriteLine("Connect to another host:");
            Console.WriteLine($"{exeName} --host 192.168.0.105 --port 5004");
            Console.WriteLine();
            Console.WriteLine("Listen connections at UDP port 5006:");
            Console.WriteLine($"{exeName} --serverPort 5006");
            return;
        }

        // ctrl-C to exit
        Console.CancelKeyPress += Console_CancelKeyPress;

        // Create Instance
        server = new RtpMidiServer("SessionName", serverPort, new ConnectionListener(), new MidiEventHandler());

        // Listen connections
        server.Start();
        var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var address in hostEntry.AddressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                Console.WriteLine($"Listening IP Address: {address}, with UDP port {serverPort}.");
            }
        }

        // Connect to the specified host and port
        if (!string.IsNullOrEmpty(host))
        {
            Console.WriteLine($"Connecting to the host: {host}:{port}");
            server.ConnectToListener(new IPEndPoint(IPAddress.Parse(host), port));
        }

        Console.WriteLine($"ctrl-C to exit");
        while (isRunning)
        {
            Thread.Sleep(100);
        }
    }

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (server != null)
        {
            server.Stop();
            server = null;
        }

        e.Cancel = true;
        isRunning = false;
    }
}

internal class ConnectionListener : IRtpMidiDeviceConnectionListener
{
    public void OnRtpMidiDeviceAttached(string deviceId)
    {
        Console.WriteLine($"deviceId: {deviceId} is attached");
    }

    public void OnRtpMidiDeviceDetached(string deviceId)
    {
        Console.WriteLine($"deviceId: {deviceId} is detached");
    }
}

internal class MidiEventHandler : IRtpMidiEventHandler
{
    public void OnMidiActiveSensing(string deviceId)
    {
        Console.WriteLine($"OnMidiActiveSensing from {deviceId}");
    }

    public void OnMidiChannelAftertouch(string deviceId, int channel, int pressure)
    {
        Console.WriteLine($"OnMidiChannelAftertouch from {deviceId}, channel: {channel}, pressure: {pressure}");
    }

    public void OnMidiContinue(string deviceId)
    {
        Console.WriteLine($"OnMidiContinue from {deviceId}");
    }

    public void OnMidiControlChange(string deviceId, int channel, int function, int value)
    {
        Console.WriteLine($"OnMidiControlChange from {deviceId}, channel: {channel}, function: {function}, value: {value}");
    }

    public void OnMidiNoteOff(string deviceId, int channel, int note, int velocity)
    {
        Console.WriteLine($"OnMidiNoteOff from {deviceId}, channel: {channel}, note: {note}, velocity: {velocity}");
    }

    public void OnMidiNoteOn(string deviceId, int channel, int note, int velocity)
    {
        Console.WriteLine($"OnMidiNoteOn from {deviceId}, channel: {channel}, note: {note}, velocity: {velocity}");
    }

    public void OnMidiPitchWheel(string deviceId, int channel, int amount)
    {
        Console.WriteLine($"OnMidiPitchWheel from {deviceId}, channel: {channel}, amount: {amount}");
    }

    public void OnMidiPolyphonicAftertouch(string deviceId, int channel, int note, int pressure)
    {
        Console.WriteLine($"OnMidiPolyphonicAftertouch from {deviceId}, channel: {channel}, note: {note}, pressure: {pressure}");
    }

    public void OnMidiProgramChange(string deviceId, int channel, int program)
    {
        Console.WriteLine($"OnMidiProgramChange from {deviceId}, channel: {channel}, program: {program}");
    }

    public void OnMidiReset(string deviceId)
    {
        Console.WriteLine($"OnMidiReset from {deviceId}");
    }

    public void OnMidiSongPositionPointer(string deviceId, int position)
    {
        Console.WriteLine($"OnMidiSongPositionPointer from {deviceId}, position: {position}");
    }

    public void OnMidiSongSelect(string deviceId, int song)
    {
        Console.WriteLine($"OnMidiSongSelect from {deviceId}, song: {song}");
    }

    public void OnMidiStart(string deviceId)
    {
        Console.WriteLine($"OnMidiStart from {deviceId}");
    }

    public void OnMidiStop(string deviceId)
    {
        Console.WriteLine($"OnMidiStop from {deviceId}");
    }

    public void OnMidiSystemExclusive(string deviceId, byte[] systemExclusive)
    {
        Console.WriteLine($"OnMidiSystemExclusive from {deviceId}, systemExclusive: {string.Join(", ", systemExclusive)}");
    }

    public void OnMidiTimeCodeQuarterFrame(string deviceId, int timing)
    {
        Console.WriteLine($"OnMidiTimeCodeQuarterFrame from {deviceId}, timing: {timing}");
    }

    public void OnMidiTimingClock(string deviceId)
    {
        Console.WriteLine($"OnMidiTimingClock from {deviceId}");
    }

    public void OnMidiTuneRequest(string deviceId)
    {
        Console.WriteLine($"OnMidiTuneRequest from {deviceId}");
    }
}