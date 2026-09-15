using System.Net;
using Makaretu.Dns;
using Xunit;

namespace jp.kshoji.rtpmidi.tests
{
    public sealed class RtpMidiZeroconfHelpersTests
    {
        [Fact]
        public void IsAppleMidiServiceInstance_DetectsServiceType()
        {
            Assert.True(RtpMidiZeroconfHelpers.IsAppleMidiServiceInstance("Studio._apple-midi._udp.local"));
            Assert.False(RtpMidiZeroconfHelpers.IsAppleMidiServiceInstance("Studio._midi2._udp.local"));
        }

        [Fact]
        public void GetServiceInstanceDisplayName_UsesFirstLabel()
        {
            var name = new DomainName("My session._apple-midi._udp.local");
            Assert.Equal("My session", RtpMidiZeroconfHelpers.GetServiceInstanceDisplayName(name));
        }

        [Fact]
        public void SelectPreferredAddress_PrefersIpv4()
        {
            var preferred = RtpMidiZeroconfHelpers.SelectPreferredAddress(new[]
            {
                IPAddress.Parse("fe80::1"),
                IPAddress.Parse("192.168.0.20")
            });

            Assert.Equal(IPAddress.Parse("192.168.0.20"), preferred);
        }

        [Fact]
        public void SelectPreferredAddress_FallsBackToIpv6()
        {
            var preferred = RtpMidiZeroconfHelpers.SelectPreferredAddress(new[]
            {
                IPAddress.Parse("fe80::1")
            });

            Assert.Equal(IPAddress.Parse("fe80::1"), preferred);
        }

        [Fact]
        public void IsSelfAdvertisement_MatchesNameAndPort()
        {
            Assert.True(RtpMidiZeroconfHelpers.IsSelfAdvertisement(
                "Studio",
                5004,
                IPAddress.Parse("10.0.0.2"),
                "Studio",
                5004,
                new[] { IPAddress.Parse("192.168.0.1") }));
        }

        [Fact]
        public void IsSelfAdvertisement_MatchesLocalAddressAndOwnPort()
        {
            Assert.True(RtpMidiZeroconfHelpers.IsSelfAdvertisement(
                "OtherName",
                5004,
                IPAddress.Parse("192.168.0.1"),
                "Studio",
                5004,
                new[] { IPAddress.Parse("192.168.0.1") }));
        }

        [Fact]
        public void IsSelfAdvertisement_AllowsSameHostDifferentPort()
        {
            Assert.False(RtpMidiZeroconfHelpers.IsSelfAdvertisement(
                "OtherSession",
                5006,
                IPAddress.Parse("192.168.0.1"),
                "Studio",
                5004,
                new[] { IPAddress.Parse("192.168.0.1") }));
        }
    }
}
