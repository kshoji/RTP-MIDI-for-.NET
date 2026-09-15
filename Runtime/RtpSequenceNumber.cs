namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// 16-bit RTP sequence numbers with a 32-bit extended form (RFC 3550 Appendix A.1).
    /// </summary>
    public static class RtpSequenceNumber
    {
        /// <summary>
        /// Extends a 16-bit sequence number using the previous extended value's wrap count.
        /// </summary>
        public static uint Extend(uint previousExtended, ushort sequence)
        {
            var previousSequence = (ushort)previousExtended;
            var udelta = (ushort)(sequence - previousSequence);
            if (udelta < 0x8000u)
            {
                return previousExtended + udelta;
            }

            return previousExtended - (ushort)(previousSequence - sequence);
        }

        /// <summary>
        /// Serial distance from <paramref name="from"/> to <paramref name="to"/> in (-32768, 32767].
        /// Positive means <paramref name="to"/> is ahead of <paramref name="from"/>.
        /// </summary>
        public static int SerialDelta(ushort from, ushort to)
        {
            return (short)(to - from);
        }

        /// <summary>
        /// True if <paramref name="candidate"/> is serially ahead of <paramref name="reference"/>.
        /// </summary>
        public static bool IsAheadOf(ushort candidate, ushort reference)
        {
            return SerialDelta(reference, candidate) > 0;
        }

        /// <summary>
        /// Low 16 bits of an extended sequence number.
        /// </summary>
        public static ushort Low16(uint extended)
        {
            return (ushort)extended;
        }
    }
}
