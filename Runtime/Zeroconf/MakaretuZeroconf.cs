using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Makaretu.Dns;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Makaretu.Dns (net-mdns) implementation of <see cref="IRtpMidiZeroconf"/>.
    /// </summary>
    public sealed class MakaretuZeroconf : IRtpMidiZeroconf
    {
        private const int BrowseIntervalMilliseconds = 5000;

        private readonly object gate = new object();
        private readonly object browseWait = new object();
        private readonly Dictionary<string, string> hostToServiceInstance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> hostToControlPort = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<IPAddress>> hostAddresses = new Dictionary<string, HashSet<IPAddress>>(StringComparer.OrdinalIgnoreCase);
        // Keyed by fully-qualified service instance so identical Directory names on different hosts can coexist.
        private readonly Dictionary<string, RtpMidiDiscoveredService> resolvedByServiceInstance =
            new Dictionary<string, RtpMidiDiscoveredService>(StringComparer.OrdinalIgnoreCase);

        private ServiceDiscovery serviceDiscovery;
        private ServiceProfile advertisedProfile;
        private string advertisedDisplayName;
        private int? advertisedControlPort;
        private IRtpMidiServiceDiscoveryListener browseListener;
        private Thread browseThread;
        private volatile bool browseRunning;
        private bool browseHandlersAttached;
        private bool networkHandlersAttached;
        private bool disposed;
        private bool readvertising;

        /// <summary>
        /// Builds an <c>_apple-midi._udp</c> profile with empty TXT and IPv4 addresses when possible.
        /// </summary>
        public static ServiceProfile CreateAppleMidiServiceProfile(
            string serviceInstanceName,
            ushort controlPort,
            IEnumerable<IPAddress> addresses = null)
        {
            if (string.IsNullOrWhiteSpace(serviceInstanceName))
            {
                throw new ArgumentException("Service instance name is required.", nameof(serviceInstanceName));
            }

            var selectedAddresses = SelectIpv4Addresses(addresses);
            var profile = new ServiceProfile(
                serviceInstanceName,
                RtpMidiDnsSdConstants.ServiceType,
                controlPort,
                selectedAddresses);

            ReplaceWithEmptyTxt(profile);
            return profile;
        }

        /// <inheritdoc />
        public void Advertise(string serviceInstanceName, int controlPort)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(serviceInstanceName))
            {
                throw new ArgumentException("Service instance name is required.", nameof(serviceInstanceName));
            }

            if (controlPort < 0 || controlPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(controlPort), controlPort, "Control port must be 0-65535.");
            }

            lock (gate)
            {
                EnsureServiceDiscoveryLocked();

                if (advertisedProfile != null)
                {
                    serviceDiscovery.Unadvertise(advertisedProfile);
                    advertisedProfile = null;
                }

                advertisedProfile = CreateAppleMidiServiceProfile(serviceInstanceName, (ushort)controlPort);
                advertisedDisplayName = serviceInstanceName;
                advertisedControlPort = controlPort;
                serviceDiscovery.Advertise(advertisedProfile);
            }
        }

        /// <inheritdoc />
        public void WithdrawAdvertisement()
        {
            ThrowIfDisposed();

            lock (gate)
            {
                if (serviceDiscovery == null || advertisedProfile == null)
                {
                    advertisedDisplayName = null;
                    advertisedControlPort = null;
                    return;
                }

                serviceDiscovery.Unadvertise(advertisedProfile);
                advertisedProfile = null;
                advertisedDisplayName = null;
                advertisedControlPort = null;
            }
        }

        /// <inheritdoc />
        public void StartBrowse(IRtpMidiServiceDiscoveryListener listener)
        {
            ThrowIfDisposed();

            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            StopBrowse();

            lock (gate)
            {
                EnsureServiceDiscoveryLocked();
                browseListener = listener;
                AttachBrowseHandlersLocked();
                ClearResolveStateLocked();
            }

            browseRunning = true;
            browseThread = new Thread(BrowseLoop)
            {
                IsBackground = true,
                Name = "RtpMidi-MakaretuZeroconf-Browse"
            };
            browseThread.Start();
        }

        /// <inheritdoc />
        public void StopBrowse()
        {
            ThrowIfDisposed();
            StopBrowseCore();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            StopBrowseCore();

            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;

                try
                {
                    if (serviceDiscovery != null && advertisedProfile != null)
                    {
                        serviceDiscovery.Unadvertise(advertisedProfile);
                    }
                }
                catch
                {
                    // Best-effort goodbye.
                }

                advertisedProfile = null;
                advertisedDisplayName = null;
                advertisedControlPort = null;
                DetachNetworkHandlersLocked();
                serviceDiscovery?.Dispose();
                serviceDiscovery = null;
            }
        }

        private void StopBrowseCore()
        {
            browseRunning = false;
            lock (browseWait)
            {
                Monitor.PulseAll(browseWait);
            }

            var thread = browseThread;
            browseThread = null;
            if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
            {
                try
                {
                    thread.Join(1000);
                }
                catch
                {
                    // Ignore join failures on shutdown.
                }
            }

            lock (gate)
            {
                DetachBrowseHandlersLocked();
                browseListener = null;
                ClearResolveStateLocked();
            }
        }

        private void BrowseLoop()
        {
            while (browseRunning)
            {
                try
                {
                    ServiceDiscovery discovery;
                    lock (gate)
                    {
                        discovery = serviceDiscovery;
                    }

                    discovery?.QueryServiceInstances(RtpMidiDnsSdConstants.ServiceType);
                }
                catch
                {
                    // Keep browsing despite transient mDNS errors.
                }

                lock (browseWait)
                {
                    if (!browseRunning)
                    {
                        break;
                    }

                    Monitor.Wait(browseWait, BrowseIntervalMilliseconds);
                }
            }
        }

        private void AttachBrowseHandlersLocked()
        {
            if (browseHandlersAttached || serviceDiscovery == null)
            {
                return;
            }

            serviceDiscovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
            serviceDiscovery.ServiceInstanceShutdown += OnServiceInstanceShutdown;
            serviceDiscovery.Mdns.AnswerReceived += OnAnswerReceived;
            browseHandlersAttached = true;
        }

        private void DetachBrowseHandlersLocked()
        {
            if (!browseHandlersAttached || serviceDiscovery == null)
            {
                browseHandlersAttached = false;
                return;
            }

            serviceDiscovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
            serviceDiscovery.ServiceInstanceShutdown -= OnServiceInstanceShutdown;
            serviceDiscovery.Mdns.AnswerReceived -= OnAnswerReceived;
            browseHandlersAttached = false;
        }

        private void ClearResolveStateLocked()
        {
            hostToServiceInstance.Clear();
            hostToControlPort.Clear();
            hostAddresses.Clear();
            resolvedByServiceInstance.Clear();
        }

        private void OnNetworkInterfaceDiscovered(object sender, NetworkInterfaceEventArgs e)
        {
            string name;
            int? port;
            lock (gate)
            {
                if (disposed || advertisedProfile == null || readvertising)
                {
                    return;
                }

                name = advertisedDisplayName;
                port = advertisedControlPort;
                if (string.IsNullOrEmpty(name) || port == null)
                {
                    return;
                }

                readvertising = true;
            }

            try
            {
                // Refresh A/AAAA records after NIC change without stopping the session.
                Advertise(name, port.Value);
            }
            catch
            {
                // Keep previous advertisement if refresh fails.
            }
            finally
            {
                lock (gate)
                {
                    readvertising = false;
                }
            }
        }

        private void OnServiceInstanceDiscovered(object sender, ServiceInstanceDiscoveryEventArgs args)
        {
            if (args?.ServiceInstanceName == null
                || !RtpMidiZeroconfHelpers.IsAppleMidiServiceInstance(args.ServiceInstanceName.ToString()))
            {
                return;
            }

            ServiceDiscovery discovery;
            lock (gate)
            {
                discovery = serviceDiscovery;
            }

            discovery?.Mdns.SendQuery(args.ServiceInstanceName, type: DnsType.SRV);
        }

        private void OnServiceInstanceShutdown(object sender, ServiceInstanceShutdownEventArgs args)
        {
            if (args?.ServiceInstanceName == null
                || !RtpMidiZeroconfHelpers.IsAppleMidiServiceInstance(args.ServiceInstanceName.ToString()))
            {
                return;
            }

            var serviceInstance = args.ServiceInstanceName.ToString();
            var displayName = RtpMidiZeroconfHelpers.GetServiceInstanceDisplayName(args.ServiceInstanceName);
            IRtpMidiServiceDiscoveryListener listener = null;
            var removed = false;

            lock (gate)
            {
                removed = resolvedByServiceInstance.Remove(serviceInstance);
                RemoveHostMappingsForServiceInstanceLocked(serviceInstance);
                listener = removed ? browseListener : null;
            }

            if (removed)
            {
                try
                {
                    listener?.OnServiceDisappeared(displayName);
                }
                catch
                {
                    // Listener exceptions must not break discovery.
                }
            }
        }

        private void OnAnswerReceived(object sender, MessageEventArgs e)
        {
            if (e?.Message == null)
            {
                return;
            }

            ProcessSrvRecords(e.Message);
            ProcessAddressRecords(e.Message);
        }

        private void ProcessSrvRecords(Message message)
        {
            foreach (var server in message.Answers.OfType<SRVRecord>())
            {
                if (server?.Name == null || server.Target == null
                    || !RtpMidiZeroconfHelpers.IsAppleMidiServiceInstance(server.Name.ToString()))
                {
                    continue;
                }

                var hostKey = server.Target.ToString();
                var serviceInstance = server.Name.ToString();

                ServiceDiscovery discovery;
                lock (gate)
                {
                    hostToServiceInstance[hostKey] = serviceInstance;
                    hostToControlPort[hostKey] = server.Port;
                    discovery = serviceDiscovery;
                }

                // Prefer A; still query AAAA as fallback when no IPv4 arrives.
                discovery?.Mdns.SendQuery(server.Target, type: DnsType.A);
                discovery?.Mdns.SendQuery(server.Target, type: DnsType.AAAA);
            }
        }

        private void ProcessAddressRecords(Message message)
        {
            foreach (var addressRecord in message.Answers.OfType<AddressRecord>())
            {
                if (addressRecord?.Name == null || addressRecord.Address == null)
                {
                    continue;
                }

                var hostKey = addressRecord.Name.ToString();
                IRtpMidiServiceDiscoveryListener listener = null;
                RtpMidiDiscoveredService appeared = null;

                lock (gate)
                {
                    if (!hostToServiceInstance.TryGetValue(hostKey, out var serviceInstance)
                        || !hostToControlPort.TryGetValue(hostKey, out var controlPort))
                    {
                        continue;
                    }

                    if (!hostAddresses.TryGetValue(hostKey, out var addresses))
                    {
                        addresses = new HashSet<IPAddress>();
                        hostAddresses[hostKey] = addresses;
                    }

                    addresses.Add(addressRecord.Address);

                    var displayName = RtpMidiZeroconfHelpers.GetServiceInstanceDisplayName(new DomainName(serviceInstance));
                    if (RtpMidiZeroconfHelpers.IsSelfAdvertisement(
                            displayName,
                            controlPort,
                            addressRecord.Address,
                            advertisedDisplayName,
                            advertisedControlPort,
                            MulticastService.GetIPAddresses()))
                    {
                        continue;
                    }

                    var preferred = RtpMidiZeroconfHelpers.SelectPreferredAddress(addresses);
                    if (preferred == null)
                    {
                        continue;
                    }

                    // Prefer sticking with IPv4 once chosen; allow upgrade from AAAA -> A.
                    if (resolvedByServiceInstance.TryGetValue(serviceInstance, out var existing))
                    {
                        var existingIsIpv4 = existing.ControlEndPoint.AddressFamily == AddressFamily.InterNetwork;
                        var preferredIsIpv4 = preferred.AddressFamily == AddressFamily.InterNetwork;
                        if (existingIsIpv4 && !preferredIsIpv4)
                        {
                            continue;
                        }

                        if (existing.ControlEndPoint.Address.Equals(preferred)
                            && existing.ControlEndPoint.Port == controlPort)
                        {
                            continue;
                        }
                    }

                    var resolved = addresses.ToArray();
                    appeared = new RtpMidiDiscoveredService(
                        displayName,
                        hostKey,
                        new IPEndPoint(preferred, controlPort),
                        resolved);
                    resolvedByServiceInstance[serviceInstance] = appeared;
                    listener = browseListener;
                }

                if (appeared != null)
                {
                    try
                    {
                        listener?.OnServiceAppeared(appeared);
                    }
                    catch
                    {
                        // Listener exceptions must not break discovery.
                    }
                }
            }
        }

        private void RemoveHostMappingsForServiceInstanceLocked(string serviceInstance)
        {
            var hostsToRemove = hostToServiceInstance
                .Where(pair => string.Equals(pair.Value, serviceInstance, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToList();

            foreach (var host in hostsToRemove)
            {
                hostToServiceInstance.Remove(host);
                hostToControlPort.Remove(host);
                hostAddresses.Remove(host);
            }
        }

        private void EnsureServiceDiscoveryLocked()
        {
            if (serviceDiscovery == null)
            {
                serviceDiscovery = new ServiceDiscovery();
                serviceDiscovery.Mdns.NetworkInterfaceDiscovered += OnNetworkInterfaceDiscovered;
                networkHandlersAttached = true;
            }
        }

        private void DetachNetworkHandlersLocked()
        {
            if (!networkHandlersAttached || serviceDiscovery?.Mdns == null)
            {
                networkHandlersAttached = false;
                return;
            }

            serviceDiscovery.Mdns.NetworkInterfaceDiscovered -= OnNetworkInterfaceDiscovered;
            networkHandlersAttached = false;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MakaretuZeroconf));
            }
        }

        private static IEnumerable<IPAddress> SelectIpv4Addresses(IEnumerable<IPAddress> addresses)
        {
            IEnumerable<IPAddress> source = addresses ?? MulticastService.GetLinkLocalAddresses();
            var ipv4 = source
                .Where(a => a != null && a.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToArray();

            return ipv4.Length > 0 ? ipv4 : null;
        }

        private static void ReplaceWithEmptyTxt(ServiceProfile profile)
        {
            foreach (var txt in profile.Resources.OfType<TXTRecord>().ToList())
            {
                profile.Resources.Remove(txt);
            }

            profile.Resources.Add(new TXTRecord
            {
                Name = profile.FullyQualifiedName,
                Strings = { string.Empty }
            });
        }
    }
}
