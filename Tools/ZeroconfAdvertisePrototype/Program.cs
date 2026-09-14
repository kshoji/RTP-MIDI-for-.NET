using System.Linq;
using System.Net;
using Makaretu.Dns;

namespace jp.kshoji.rtpmidi.tools;

/// <summary>
/// Phase 0 smoke test: advertise an empty-TXT <c>_apple-midi._udp</c> service.
/// Verify visibility in macOS Audio MIDI Setup Directory, Tobias rtpMIDI, or <c>dns-sd -B _apple-midi._udp</c>.
/// </summary>
internal static class Program
{
    private const string ServiceType = "_apple-midi._udp";

    private static int Main(string[] args)
    {
        var sessionName = args.Length > 0 ? args[0] : "RTP-MIDI Phase0 Prototype";
        var controlPort = args.Length > 1 && ushort.TryParse(args[1], out var parsed) ? parsed : (ushort)5004;

        Console.WriteLine($"Advertising \"{sessionName}\" as {ServiceType} on control port {controlPort}");
        Console.WriteLine("TXT: empty (no keys). Data port is control+1 by convention and is not advertised.");
        Console.WriteLine("Press Enter to withdraw and exit.");

        using var discovery = new ServiceDiscovery();
        var profile = CreateAppleMidiProfile(sessionName, controlPort);

        PrintProfileSummary(profile);
        discovery.Advertise(profile);

        Console.ReadLine();

        discovery.Unadvertise(profile);
        Console.WriteLine("Advertisement withdrawn.");
        return 0;
    }

    /// <summary>
    /// Builds a ServiceProfile for Apple Network MIDI / rtpMIDI.
    /// Clears the Makaretu default <c>txtvers=1</c> so TXT has no keys (interop requirement).
    /// </summary>
    internal static ServiceProfile CreateAppleMidiProfile(string sessionName, ushort controlPort)
    {
        // Prefer IPv4 link-local / unicast addresses that MulticastService already filters.
        var ipv4 = MulticastService.GetLinkLocalAddresses()
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .ToArray();

        var profile = new ServiceProfile(
            sessionName,
            ServiceType,
            controlPort,
            ipv4.Length > 0 ? ipv4 : null);

        // ServiceProfile ctor adds TXT with "txtvers=1". Replace with an empty TXT RR (no keys).
        foreach (var txt in profile.Resources.OfType<TXTRecord>().ToList())
        {
            profile.Resources.Remove(txt);
        }

        profile.Resources.Add(new TXTRecord
        {
            Name = profile.FullyQualifiedName,
            Strings = { string.Empty }
        });

        return profile;
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
