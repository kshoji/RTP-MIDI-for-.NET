namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// DNS-SD constants for Apple Network MIDI / rtpMIDI (<c>_apple-midi._udp</c>).
    /// </summary>
    public static class RtpMidiDnsSdConstants
    {
        /// <summary>
        /// DNS-SD service type (without domain).
        /// </summary>
        public const string ServiceType = "_apple-midi._udp";

        /// <summary>
        /// DNS-SD domain for link-local discovery.
        /// </summary>
        public const string ServiceDomain = "local.";
    }
}
