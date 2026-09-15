using System;
using System.Collections.Generic;
using System.Net;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// In-memory <see cref="IRtpMidiZeroconf"/> for unit tests.
    /// Advertise / browse state is local to the instance; use <see cref="Publish"/> / <see cref="Unpublish"/>
    /// to simulate remote Directory entries.
    /// </summary>
    public sealed class FakeRtpMidiZeroconf : IRtpMidiZeroconf
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, RtpMidiDiscoveredService> knownServices =
            new Dictionary<string, RtpMidiDiscoveredService>(StringComparer.Ordinal);
        private IRtpMidiServiceDiscoveryListener browseListener;
        private bool disposed;

        /// <summary>
        /// Whether an advertisement is currently active.
        /// </summary>
        public bool IsAdvertising { get; private set; }

        /// <summary>
        /// Advertised DNS-SD instance name, or <c>null</c> when not advertising.
        /// </summary>
        public string AdvertisedServiceName { get; private set; }

        /// <summary>
        /// Advertised control port, or <c>-1</c> when not advertising.
        /// </summary>
        public int AdvertisedControlPort { get; private set; } = -1;

        /// <summary>
        /// Whether browse is active.
        /// </summary>
        public bool IsBrowsing { get; private set; }

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
                AdvertisedServiceName = serviceInstanceName;
                AdvertisedControlPort = controlPort;
                IsAdvertising = true;
            }
        }

        /// <inheritdoc />
        public void WithdrawAdvertisement()
        {
            ThrowIfDisposed();

            lock (gate)
            {
                IsAdvertising = false;
                AdvertisedServiceName = null;
                AdvertisedControlPort = -1;
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

            List<RtpMidiDiscoveredService> snapshot;
            lock (gate)
            {
                browseListener = listener;
                IsBrowsing = true;
                snapshot = new List<RtpMidiDiscoveredService>(knownServices.Values);
            }

            foreach (var service in snapshot)
            {
                listener.OnServiceAppeared(service);
            }
        }

        /// <inheritdoc />
        public void StopBrowse()
        {
            ThrowIfDisposed();

            lock (gate)
            {
                IsBrowsing = false;
                browseListener = null;
            }
        }

        /// <summary>
        /// Simulates a remote service becoming available.
        /// Notifies the browse listener when browsing is active.
        /// </summary>
        public void Publish(RtpMidiDiscoveredService service)
        {
            ThrowIfDisposed();

            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            IRtpMidiServiceDiscoveryListener listener;
            lock (gate)
            {
                knownServices[service.ServiceName] = service;
                listener = IsBrowsing ? browseListener : null;
            }

            listener?.OnServiceAppeared(service);
        }

        /// <summary>
        /// Convenience overload to publish a remote service.
        /// </summary>
        public void Publish(string serviceName, string hostName, IPEndPoint controlEndPoint)
        {
            Publish(new RtpMidiDiscoveredService(serviceName, hostName, controlEndPoint));
        }

        /// <summary>
        /// Simulates a remote service disappearing.
        /// Notifies the browse listener when browsing is active.
        /// </summary>
        public void Unpublish(string serviceName)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(serviceName))
            {
                throw new ArgumentException("Service name is required.", nameof(serviceName));
            }

            IRtpMidiServiceDiscoveryListener listener;
            var removed = false;
            lock (gate)
            {
                removed = knownServices.Remove(serviceName);
                listener = removed && IsBrowsing ? browseListener : null;
            }

            if (removed)
            {
                listener?.OnServiceDisappeared(serviceName);
            }
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
                IsBrowsing = false;
                browseListener = null;
                IsAdvertising = false;
                AdvertisedServiceName = null;
                AdvertisedControlPort = -1;
                knownServices.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(FakeRtpMidiZeroconf));
            }
        }
    }
}
