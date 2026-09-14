using System;
using System.Collections.Generic;
using System.Net;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// A resolved <c>_apple-midi._udp</c> service suitable for <see cref="RtpMidiServer.ConnectToListener"/>.
    /// </summary>
    public sealed class RtpMidiDiscoveredService
    {
        /// <summary>
        /// Creates a discovered-service snapshot.
        /// </summary>
        /// <param name="serviceName">DNS-SD service instance name (Directory display name).</param>
        /// <param name="hostName">SRV target host name.</param>
        /// <param name="controlEndPoint">Resolved control-port endpoint (data port is control + 1).</param>
        /// <param name="resolvedAddresses">Optional addresses used while resolving (debug).</param>
        public RtpMidiDiscoveredService(
            string serviceName,
            string hostName,
            IPEndPoint controlEndPoint,
            IReadOnlyList<IPAddress> resolvedAddresses = null)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                throw new ArgumentException("Service name is required.", nameof(serviceName));
            }

            if (string.IsNullOrWhiteSpace(hostName))
            {
                throw new ArgumentException("Host name is required.", nameof(hostName));
            }

            ServiceName = serviceName;
            HostName = hostName;
            ControlEndPoint = controlEndPoint ?? throw new ArgumentNullException(nameof(controlEndPoint));
            ResolvedAddresses = resolvedAddresses ?? Array.Empty<IPAddress>();
        }

        /// <summary>
        /// DNS-SD service instance name.
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// SRV target host name.
        /// </summary>
        public string HostName { get; }

        /// <summary>
        /// Control-port endpoint. Pass to <see cref="RtpMidiServer.ConnectToListener"/>.
        /// </summary>
        public IPEndPoint ControlEndPoint { get; }

        /// <summary>
        /// Addresses observed during resolve (may be empty).
        /// </summary>
        public IReadOnlyList<IPAddress> ResolvedAddresses { get; }
    }
}
