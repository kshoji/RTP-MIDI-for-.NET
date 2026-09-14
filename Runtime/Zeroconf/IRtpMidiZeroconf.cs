using System;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Pluggable DNS-SD advertise / browse for RTP-MIDI.
    /// First implementation will use Makaretu.Dns; tests can inject <see cref="FakeRtpMidiZeroconf"/>.
    /// </summary>
    public interface IRtpMidiZeroconf : IDisposable
    {
        /// <summary>
        /// Advertises a local session as <see cref="RtpMidiDnsSdConstants.ServiceType"/> on the control port.
        /// </summary>
        /// <param name="serviceInstanceName">DNS-SD service instance name (typically the session name).</param>
        /// <param name="controlPort">AppleMIDI control port (data port is control + 1 and is not advertised).</param>
        void Advertise(string serviceInstanceName, int controlPort);

        /// <summary>
        /// Withdraws the current advertisement, if any.
        /// </summary>
        void WithdrawAdvertisement();

        /// <summary>
        /// Starts browsing for <see cref="RtpMidiDnsSdConstants.ServiceType"/> services.
        /// </summary>
        /// <param name="listener">Discovery callbacks.</param>
        void StartBrowse(IRtpMidiServiceDiscoveryListener listener);

        /// <summary>
        /// Stops browsing. Does not withdraw advertisements.
        /// </summary>
        void StopBrowse();
    }
}
