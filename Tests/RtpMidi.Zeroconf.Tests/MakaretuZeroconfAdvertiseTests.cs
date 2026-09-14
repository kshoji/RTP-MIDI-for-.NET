using System.Linq;
using System.Net;
using Makaretu.Dns;
using Xunit;

namespace jp.kshoji.rtpmidi.tests
{
    public sealed class MakaretuZeroconfProfileTests
    {
        [Fact]
        public void CreateAppleMidiServiceProfile_UsesAppleMidiServiceType()
        {
            var profile = MakaretuZeroconf.CreateAppleMidiServiceProfile(
                "Studio",
                5004,
                new[] { IPAddress.Parse("192.168.0.10") });

            Assert.Equal(RtpMidiDnsSdConstants.ServiceType, profile.ServiceName.ToString());
            Assert.Contains("_apple-midi._udp", profile.FullyQualifiedName.ToString());
        }

        [Fact]
        public void CreateAppleMidiServiceProfile_AdvertisesControlPortOnly()
        {
            var profile = MakaretuZeroconf.CreateAppleMidiServiceProfile(
                "Studio",
                5004,
                new[] { IPAddress.Parse("192.168.0.10") });

            var srv = Assert.Single(profile.Resources.OfType<SRVRecord>());
            Assert.Equal(5004, srv.Port);
            Assert.DoesNotContain(profile.Resources.OfType<SRVRecord>(), r => r.Port == 5005);
        }

        [Fact]
        public void CreateAppleMidiServiceProfile_UsesEmptyTxtWithoutKeys()
        {
            var profile = MakaretuZeroconf.CreateAppleMidiServiceProfile(
                "Studio",
                5004,
                new[] { IPAddress.Parse("192.168.0.10") });

            var txt = Assert.Single(profile.Resources.OfType<TXTRecord>());
            Assert.True(
                txt.Strings.Count == 0
                || (txt.Strings.Count == 1 && string.IsNullOrEmpty(txt.Strings[0])));
            Assert.DoesNotContain(txt.Strings, s => s != null && s.StartsWith("txtvers="));
            Assert.DoesNotContain(txt.Strings, s => s != null && s.Contains("UMPEndpointName"));
            Assert.DoesNotContain(txt.Strings, s => s != null && s.Contains("ProductInstanceId"));
        }

        [Fact]
        public void CreateAppleMidiServiceProfile_PrefersIpv4Addresses()
        {
            var profile = MakaretuZeroconf.CreateAppleMidiServiceProfile(
                "Studio",
                5004,
                new[]
                {
                    IPAddress.Parse("192.168.0.10"),
                    IPAddress.Parse("fe80::1")
                });

            Assert.Contains(profile.Resources.OfType<ARecord>(), a => a.Address.Equals(IPAddress.Parse("192.168.0.10")));
            Assert.Empty(profile.Resources.OfType<AAAARecord>());
        }
    }

    public sealed class RtpMidiServerAdvertiseTests
    {
        [Fact]
        public void Start_AdvertisesViaInjectedZeroconf()
        {
            using var fake = new FakeRtpMidiZeroconf();
            var server = new RtpMidiServer(
                "My session",
                5004,
                new NullConnectionListener(),
                new NullEventHandler(),
                fake);

            try
            {
                server.Start();

                Assert.True(fake.IsAdvertising);
                Assert.Equal("My session", fake.AdvertisedServiceName);
                Assert.Equal(5004, fake.AdvertisedControlPort);
            }
            finally
            {
                server.Stop();
            }

            Assert.False(fake.IsAdvertising);
        }

        [Fact]
        public void Start_SkipsAdvertiseWhenDisabled()
        {
            using var fake = new FakeRtpMidiZeroconf();
            var server = new RtpMidiServer(
                "My session",
                5004,
                new NullConnectionListener(),
                new NullEventHandler(),
                fake,
                advertiseOnStart: false);

            try
            {
                server.Start();
                Assert.False(fake.IsAdvertising);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Start_ContinuesWhenAdvertiseThrows()
        {
            var throwing = new ThrowingAdvertiseZeroconf();
            var server = new RtpMidiServer(
                "My session",
                5004,
                new NullConnectionListener(),
                new NullEventHandler(),
                throwing);

            try
            {
                var exception = Record.Exception(() => server.Start());
                Assert.Null(exception);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void StartDiscovery_UsesInjectedZeroconf()
        {
            using var fake = new FakeRtpMidiZeroconf();
            var server = new RtpMidiServer(
                "My session",
                5004,
                new NullConnectionListener(),
                new NullEventHandler(),
                fake,
                advertiseOnStart: false);
            var listener = new RecordingDiscoveryListener();

            try
            {
                server.StartDiscovery(listener);
                Assert.True(fake.IsBrowsing);

                fake.Publish("Remote", "remote.local", new IPEndPoint(IPAddress.Loopback, 5006));
                Assert.Single(listener.Appeared);
                Assert.Equal(5006, listener.Appeared[0].ControlEndPoint.Port);

                server.StopDiscovery();
                Assert.False(fake.IsBrowsing);
            }
            finally
            {
                server.Stop();
            }
        }

        private sealed class RecordingDiscoveryListener : IRtpMidiServiceDiscoveryListener
        {
            public List<RtpMidiDiscoveredService> Appeared { get; } = new();

            public void OnServiceAppeared(RtpMidiDiscoveredService service) => Appeared.Add(service);

            public void OnServiceDisappeared(string serviceName)
            {
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

        private sealed class ThrowingAdvertiseZeroconf : IRtpMidiZeroconf
        {
            public void Advertise(string serviceInstanceName, int controlPort) =>
                throw new InvalidOperationException("advertise failed");

            public void WithdrawAdvertisement()
            {
            }

            public void StartBrowse(IRtpMidiServiceDiscoveryListener listener)
            {
            }

            public void StopBrowse()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
