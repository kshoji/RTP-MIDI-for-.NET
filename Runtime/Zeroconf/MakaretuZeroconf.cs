using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Makaretu.Dns (net-mdns) implementation of <see cref="IRtpMidiZeroconf"/>.
    /// Phase 2 covers advertise / withdraw; browse / resolve follows in Phase 3.
    /// </summary>
    public sealed class MakaretuZeroconf : IRtpMidiZeroconf
    {
        private readonly object gate = new object();
        private ServiceDiscovery serviceDiscovery;
        private ServiceProfile advertisedProfile;
        private bool disposed;

        /// <summary>
        /// Builds an <c>_apple-midi._udp</c> profile with empty TXT and IPv4 addresses when possible.
        /// </summary>
        /// <param name="serviceInstanceName">DNS-SD instance name.</param>
        /// <param name="controlPort">AppleMIDI control port.</param>
        /// <param name="addresses">
        /// Optional address list. When null, IPv4 addresses from
        /// <see cref="MulticastService.GetLinkLocalAddresses"/> are used.
        /// </param>
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
                    return;
                }

                serviceDiscovery.Unadvertise(advertisedProfile);
                advertisedProfile = null;
            }
        }

        /// <inheritdoc />
        public void StartBrowse(IRtpMidiServiceDiscoveryListener listener)
        {
            ThrowIfDisposed();
            throw new NotSupportedException("Browse/resolve is not implemented yet (Phase 3).");
        }

        /// <inheritdoc />
        public void StopBrowse()
        {
            ThrowIfDisposed();
            // No-op until Phase 3.
        }

        /// <inheritdoc />
        public void Dispose()
        {
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
                serviceDiscovery?.Dispose();
                serviceDiscovery = null;
            }
        }

        private void EnsureServiceDiscoveryLocked()
        {
            if (serviceDiscovery == null)
            {
                serviceDiscovery = new ServiceDiscovery();
            }
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

            // Fall back to Makaretu defaults (may include AAAA) only if no IPv4 is available.
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
