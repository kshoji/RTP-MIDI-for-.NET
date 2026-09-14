namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Receives Zeroconf browse / resolve notifications for <c>_apple-midi._udp</c>.
    /// Separate from <see cref="IRtpMidiDeviceConnectionListener"/> (session attach / detach).
    /// </summary>
    public interface IRtpMidiServiceDiscoveryListener
    {
        /// <summary>
        /// Called when a remote service is resolved to a control endpoint.
        /// </summary>
        /// <param name="service">Resolved service snapshot.</param>
        void OnServiceAppeared(RtpMidiDiscoveredService service);

        /// <summary>
        /// Called when a previously advertised service disappears.
        /// </summary>
        /// <param name="serviceName">DNS-SD service instance name.</param>
        void OnServiceDisappeared(string serviceName);
    }
}
