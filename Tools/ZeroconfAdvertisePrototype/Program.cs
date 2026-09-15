using Makaretu.Dns;

namespace jp.kshoji.rtpmidi.tools;

/// <summary>
/// Smoke test for <see cref="MakaretuZeroconf"/> advertise.
/// Verify visibility in macOS Audio MIDI Setup Directory, Tobias rtpMIDI, or <c>dns-sd -B _apple-midi._udp</c>.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var sessionName = args.Length > 0 ? args[0] : "RTP-MIDI Advertise Prototype";
        var controlPort = args.Length > 1 && ushort.TryParse(args[1], out var parsed) ? parsed : (ushort)5004;

        Console.WriteLine($"Advertising \"{sessionName}\" as {RtpMidiDnsSdConstants.ServiceType} on control port {controlPort}");
        Console.WriteLine("TXT: empty (no keys). Data port is control+1 by convention and is not advertised.");
        Console.WriteLine("Press Enter to withdraw and exit.");

        using var zeroconf = new MakaretuZeroconf();
        var profile = MakaretuZeroconf.CreateAppleMidiServiceProfile(sessionName, controlPort);
        PrintProfileSummary(profile);
        zeroconf.Advertise(sessionName, controlPort);

        Console.ReadLine();

        zeroconf.WithdrawAdvertisement();
        Console.WriteLine("Advertisement withdrawn.");
        return 0;
    }

    private static void PrintProfileSummary(ServiceProfile profile)
    {
        Console.WriteLine($"FQDN: {profile.FullyQualifiedName}");
        Console.WriteLine($"Host: {profile.HostName}");
        foreach (var record in profile.Resources)
        {
            switch (record)
            {
                case SRVRecord srv:
                    Console.WriteLine($"SRV port={srv.Port} target={srv.Target}");
                    break;
                case TXTRecord txt:
                    var payload = txt.Strings.Count == 0
                        || (txt.Strings.Count == 1 && string.IsNullOrEmpty(txt.Strings[0]))
                        ? "(empty)"
                        : string.Join(", ", txt.Strings);
                    Console.WriteLine($"TXT {payload}");
                    break;
                case ARecord a:
                    Console.WriteLine($"A {a.Address}");
                    break;
                case AAAARecord aaaa:
                    Console.WriteLine($"AAAA {aaaa.Address}");
                    break;
            }
        }
    }
}
