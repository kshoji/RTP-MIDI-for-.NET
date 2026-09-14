using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Shared helpers for Apple MIDI DNS-SD naming, self-exclusion, and address preference.
    /// </summary>
    public static class RtpMidiZeroconfHelpers
    {
        private static readonly string ServiceTypeMarker = "." + RtpMidiDnsSdConstants.ServiceType;

        /// <summary>
        /// Returns true when the DNS name refers to an <c>_apple-midi._udp</c> instance.
        /// </summary>
        public static bool IsAppleMidiServiceInstance(string serviceInstanceName)
        {
            return !string.IsNullOrEmpty(serviceInstanceName)
                   && serviceInstanceName.IndexOf(RtpMidiDnsSdConstants.ServiceType, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Extracts the DNS-SD instance display name (Directory name) from a fully qualified instance.
        /// </summary>
        public static string GetServiceInstanceDisplayName(DomainName serviceInstanceName)
        {
            if (serviceInstanceName == null)
            {
                throw new ArgumentNullException(nameof(serviceInstanceName));
            }

            if (serviceInstanceName.Labels.Count > 0)
            {
                return serviceInstanceName.Labels[0];
            }

            var full = serviceInstanceName.ToString();
            var index = full.IndexOf(ServiceTypeMarker, StringComparison.OrdinalIgnoreCase);
            return index > 0 ? full.Substring(0, index) : full;
        }

        /// <summary>
        /// Chooses an IPv4 address when present; otherwise the first address.
        /// </summary>
        public static IPAddress SelectPreferredAddress(IEnumerable<IPAddress> addresses)
        {
            if (addresses == null)
            {
                return null;
            }

            var list = addresses.Where(a => a != null).ToList();
            if (list.Count == 0)
            {
                return null;
            }

            return list.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? list[0];
        }

        /// <summary>
        /// Returns true when the resolved service is this process's own advertisement.
        /// </summary>
        /// <remarks>
        /// Matches by (display name + control port), or by (local address + own control port).
        /// Other sessions on the same host with a different control port are not excluded.
        /// </remarks>
        public static bool IsSelfAdvertisement(
            string serviceDisplayName,
            int controlPort,
            IPAddress resolvedAddress,
            string ownServiceDisplayName,
            int? ownControlPort,
            IEnumerable<IPAddress> localAddresses)
        {
            if (ownControlPort == null || controlPort != ownControlPort.Value)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(ownServiceDisplayName)
                && string.Equals(serviceDisplayName, ownServiceDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (resolvedAddress == null || localAddresses == null)
            {
                return false;
            }

            return localAddresses.Any(a => a != null && a.Equals(resolvedAddress));
        }
    }
}
