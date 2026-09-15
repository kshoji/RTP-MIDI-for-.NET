using System.Net;
using Xunit;

namespace jp.kshoji.rtpmidi.tests
{
    public sealed class RtpMidiDnsSdConstantsTests
    {
        [Fact]
        public void ServiceType_IsAppleMidiUdp()
        {
            Assert.Equal("_apple-midi._udp", RtpMidiDnsSdConstants.ServiceType);
        }

        [Fact]
        public void ServiceDomain_IsLocal()
        {
            Assert.Equal("local.", RtpMidiDnsSdConstants.ServiceDomain);
        }
    }

    public sealed class RtpMidiDiscoveredServiceTests
    {
        [Fact]
        public void Constructor_StoresRequiredFields()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.0.10"), 5004);
            var addresses = new[] { IPAddress.Parse("192.168.0.10") };

            var service = new RtpMidiDiscoveredService("Studio", "host.local", endpoint, addresses);

            Assert.Equal("Studio", service.ServiceName);
            Assert.Equal("host.local", service.HostName);
            Assert.Equal(endpoint, service.ControlEndPoint);
            Assert.Equal(addresses, service.ResolvedAddresses);
        }

        [Fact]
        public void Constructor_RejectsNullControlEndPoint()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new RtpMidiDiscoveredService("Studio", "host.local", null));
        }
    }

    public sealed class FakeRtpMidiZeroconfTests
    {
        [Fact]
        public void Advertise_StoresInstanceNameAndControlPort()
        {
            using var zeroconf = new FakeRtpMidiZeroconf();

            zeroconf.Advertise("My session", 5004);

            Assert.True(zeroconf.IsAdvertising);
            Assert.Equal("My session", zeroconf.AdvertisedServiceName);
            Assert.Equal(5004, zeroconf.AdvertisedControlPort);
        }

        [Fact]
        public void WithdrawAdvertisement_ClearsState()
        {
            using var zeroconf = new FakeRtpMidiZeroconf();
            zeroconf.Advertise("My session", 5004);

            zeroconf.WithdrawAdvertisement();

            Assert.False(zeroconf.IsAdvertising);
            Assert.Null(zeroconf.AdvertisedServiceName);
            Assert.Equal(-1, zeroconf.AdvertisedControlPort);
        }

        [Fact]
        public void StartBrowse_ReplaysKnownServices()
        {
            using var zeroconf = new FakeRtpMidiZeroconf();
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5004);
            zeroconf.Publish("Remote", "remote.local", endpoint);

            var listener = new RecordingDiscoveryListener();
            zeroconf.StartBrowse(listener);

            Assert.True(zeroconf.IsBrowsing);
            Assert.Single(listener.Appeared);
            Assert.Equal("Remote", listener.Appeared[0].ServiceName);
            Assert.Equal(endpoint, listener.Appeared[0].ControlEndPoint);
        }

        [Fact]
        public void Publish_WhileBrowsing_NotifiesListener()
        {
            using var zeroconf = new FakeRtpMidiZeroconf();
            var listener = new RecordingDiscoveryListener();
            zeroconf.StartBrowse(listener);

            var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5006);
            zeroconf.Publish("Live", "live.local", endpoint);

            Assert.Single(listener.Appeared);
            Assert.Equal(5006, listener.Appeared[0].ControlEndPoint.Port);
        }

        [Fact]
        public void Unpublish_WhileBrowsing_NotifiesListener()
        {
            using var zeroconf = new FakeRtpMidiZeroconf();
            var listener = new RecordingDiscoveryListener();
            zeroconf.StartBrowse(listener);
            zeroconf.Publish("Live", "live.local", new IPEndPoint(IPAddress.Loopback, 5004));

            zeroconf.Unpublish("Live");

            Assert.Single(listener.Disappeared);
            Assert.Equal("Live", listener.Disappeared[0]);
        }

        [Fact]
        public void StopBrowse_StopsNotifications()
        {
            using var zeroconf = new FakeRtpMidiZeroconf();
            var listener = new RecordingDiscoveryListener();
            zeroconf.StartBrowse(listener);
            zeroconf.StopBrowse();

            zeroconf.Publish("Late", "late.local", new IPEndPoint(IPAddress.Loopback, 5004));

            Assert.False(zeroconf.IsBrowsing);
            Assert.Empty(listener.Appeared);
        }

        [Fact]
        public void Advertise_DoesNotAutoPublishToBrowse()
        {
            using var zeroconf = new FakeRtpMidiZeroconf();
            var listener = new RecordingDiscoveryListener();
            zeroconf.StartBrowse(listener);

            zeroconf.Advertise("Self", 5004);

            Assert.Empty(listener.Appeared);
        }

        private sealed class RecordingDiscoveryListener : IRtpMidiServiceDiscoveryListener
        {
            public List<RtpMidiDiscoveredService> Appeared { get; } = new();
            public List<string> Disappeared { get; } = new();

            public void OnServiceAppeared(RtpMidiDiscoveredService service) => Appeared.Add(service);

            public void OnServiceDisappeared(string serviceName) => Disappeared.Add(serviceName);
        }
    }
}
