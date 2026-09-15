namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// UDP payload budget for RTP header + MIDI command section + recovery journal (RFC 6295 §6).
    /// <see cref="RtpMidiParticipant.MaxBufferSize"/> limits the MIDI section; this budget is the octet guard.
    /// </summary>
    public static class RtpMidiPayloadBudget
    {
        public const int RtpHeaderLength = 12;

        public enum Layout
        {
            Combined,
            MidiThenJournal,
            Overflow,
        }

        public static int MidiCommandHeaderLength(int midiOctets)
        {
            if (midiOctets < 0)
            {
                midiOctets = 0;
            }

            return midiOctets < 0x0f ? 1 : 2;
        }

        public static int UdpPayloadLength(int midiOctets, int journalOctets)
        {
            if (midiOctets < 0)
            {
                midiOctets = 0;
            }

            if (journalOctets < 0)
            {
                journalOctets = 0;
            }

            return RtpHeaderLength + MidiCommandHeaderLength(midiOctets) + midiOctets + journalOctets;
        }

        public static bool Fits(
            int midiOctets,
            int journalOctets,
            int maxUdpPayloadSize = RtpMidiParticipant.MaxUdpPayloadSize)
        {
            return UdpPayloadLength(midiOctets, journalOctets) <= maxUdpPayloadSize;
        }

        public static Layout Choose(
            int midiOctets,
            int journalOctets,
            int maxUdpPayloadSize = RtpMidiParticipant.MaxUdpPayloadSize)
        {
            if (!Fits(0, journalOctets, maxUdpPayloadSize))
            {
                return Layout.Overflow;
            }

            if (Fits(midiOctets, journalOctets, maxUdpPayloadSize))
            {
                return Layout.Combined;
            }

            return Layout.MidiThenJournal;
        }
    }
}
