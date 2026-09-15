using System.Net;
using Xunit;

namespace jp.kshoji.rtpmidi.tests
{
    public sealed class RtpMidiZeroconfStabilityTests
    {
        [Fact]
        public void Discovery_DoesNotAutoConnect()
        {
            using var fake = new FakeRtpMidiZeroconf();
            var connected = false;
            var server = new RtpMidiServer(
                "Local",
                TestPorts.NextControlPort(),
                new NullConnectionListener(),
                new NullEventHandler(),
                fake,
                advertiseOnStart: false);

            try
            {
                server.StartDiscovery(new CallbackDiscoveryListener(
                    appeared =>
                    {
                        // Apps must opt in — sample/docs must not auto-connect here.
                        Assert.NotNull(appeared.ControlEndPoint);
                    },
                    _ => { }));

                fake.Publish("Remote", "remote.local", new IPEndPoint(IPAddress.Loopback, 5006));
                Assert.False(connected);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void AdvertiseFailure_NotifiesExceptionAndKeepsSessionStartable()
        {
            var throwing = new ThrowingAdvertiseZeroconf();
            var errors = new List<RtpMidiExceptionKind>();
            var server = new RtpMidiServer(
                "Local",
                TestPorts.NextControlPort(),
                new NullConnectionListener(),
                new NullEventHandler(),
                throwing);
            server.SetRtpMidiExceptionListener(new RecordingExceptionListener(errors));

            try
            {
                var exception = Record.Exception(() => server.Start());
                Assert.Null(exception);
                Assert.Contains(RtpMidiExceptionKind.ZeroconfException, errors);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Fake_AllowsSameDisplayNameWhenRepublishedWithNewEndpoint()
        {
            using var fake = new FakeRtpMidiZeroconf();
            var listener = new RecordingDiscoveryListener();
            fake.StartBrowse(listener);

            fake.Publish("Studio", "a.local", new IPEndPoint(IPAddress.Parse("10.0.0.1"), 5004));
            fake.Publish("Studio", "b.local", new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5006));

            // Fake replaces by service name; apps should treat latest Appeared as authoritative for that name.
            Assert.Equal(2, listener.Appeared.Count);
            Assert.Equal(5006, listener.Appeared[1].ControlEndPoint.Port);
        }

        [Fact]
        public void SelectPreferredAddress_IgnoresNullEntries()
        {
            var preferred = RtpMidiZeroconfHelpers.SelectPreferredAddress(new IPAddress[]
            {
                null,
                IPAddress.Parse("fe80::2"),
                IPAddress.Parse("192.168.1.5")
            });

            Assert.Equal(IPAddress.Parse("192.168.1.5"), preferred);
        }

        private sealed class CallbackDiscoveryListener : IRtpMidiServiceDiscoveryListener
        {
            private readonly Action<RtpMidiDiscoveredService> onAppeared;
            private readonly Action<string> onDisappeared;

            public CallbackDiscoveryListener(
                Action<RtpMidiDiscoveredService> onAppeared,
                Action<string> onDisappeared)
            {
                this.onAppeared = onAppeared;
                this.onDisappeared = onDisappeared;
            }

            public void OnServiceAppeared(RtpMidiDiscoveredService service) => onAppeared(service);

            public void OnServiceDisappeared(string serviceName) => onDisappeared(serviceName);
        }

        private sealed class RecordingDiscoveryListener : IRtpMidiServiceDiscoveryListener
        {
            public List<RtpMidiDiscoveredService> Appeared { get; } = new();

            public void OnServiceAppeared(RtpMidiDiscoveredService service) => Appeared.Add(service);

            public void OnServiceDisappeared(string serviceName)
            {
            }
        }

        private sealed class RecordingExceptionListener : IRtpMidiExceptionListener
        {
            private readonly List<RtpMidiExceptionKind> errors;

            public RecordingExceptionListener(List<RtpMidiExceptionKind> errors)
            {
                this.errors = errors;
            }

            public void OnError(RtpMidiExceptionKind exceptionKind) => errors.Add(exceptionKind);
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
    }
}
