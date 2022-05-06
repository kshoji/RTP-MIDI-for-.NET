namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Constants for RTP MIDI
    /// </summary>
    public static class RtpMidiConstants
    {
        public static readonly byte[] Signature = {0xff, 0xff};
        public static readonly byte[] ProtocolVersion = {0x00, 0x00, 0x00, 0x02};
        public static readonly byte[] Invitation = {(byte)'I', (byte)'N'};
        public static readonly byte[] EndSession = {(byte)'B', (byte)'Y'};
        public static readonly byte[] Synchronization = {(byte)'C', (byte)'K'};
        public static readonly byte[] InvitationAccepted = {(byte)'O', (byte)'K'};
        public static readonly byte[] InvitationRejected = {(byte)'N', (byte)'O'};
        public static readonly byte[] ReceiverFeedback = {(byte)'R', (byte)'S'};
        public static readonly byte[] BitrateReceiveLimit = {(byte)'R', (byte)'L'};
    }

    /// <summary>
    /// UDP Port type
    /// </summary>
    public enum PortType
    {
        Control,
        Data,
    }

    /// <summary>
    /// the message category of MIDI
    /// </summary>
    public enum MidiType
    {
        NoteOff = 0x80,
        NoteOn = 0x90,
        AfterTouchPoly = 0xa0,
        ControlChange = 0xb0,
        ProgramChange = 0xc0,
        AfterTouchChannel = 0xd0,
        PitchBend = 0xe0,
        SystemExclusive = 0xf0,
        SystemExclusiveStart = SystemExclusive,
        TimeCodeQuarterFrame = 0xf1,
        SongPosition = 0xf2,
        SongSelect = 0xF3,
        TuneRequest = 0xf6,
        SystemExclusiveEnd = 0xf7,
        Clock = 0xf8,
        Start = 0xfa,
        Continue = 0xfb,
        Stop = 0xfc,
        ActiveSensing = 0xfe,
        SystemReset = 0xff,
    }

    /// <summary>
    /// RTP MIDI invitation message
    /// </summary>
    public struct RtpMidiInvitation
    {
        public RtpMidiInvitation(int initiatorToken, int ssrc, string sessionName)
        {
            InitiatorToken = initiatorToken;
            Ssrc = ssrc;
            SessionName = sessionName;
        }

        public int InitiatorToken { get; internal set; }
        public int Ssrc { get; internal set; }
        public string SessionName { get; internal set; }
    }

    /// <summary>
    /// RTP MIDI invitation accepted message
    /// </summary>
    public struct RtpMidiInvitationAccepted
    {
        public RtpMidiInvitationAccepted(int initiatorToken, int ssrc, string sessionName)
        {
            InitiatorToken = initiatorToken;
            Ssrc = ssrc;
            SessionName = sessionName;
        }

        public int InitiatorToken { get; }

        public int Ssrc { get; }

        public string SessionName { get; }
    }

    /// <summary>
    /// RTP MIDI invitation rejected message
    /// </summary>
    public struct RtpMidiInvitationRejected
    {
        public RtpMidiInvitationRejected(int initiatorToken, int ssrc, string sessionName)
        {
            InitiatorToken = initiatorToken;
            Ssrc = ssrc;
            SessionName = sessionName;
        }

        public int InitiatorToken { get; }
        public int Ssrc { get; }
        public string SessionName { get; }
    }

    /// <summary>
    /// RTP MIDI bitrate receive limit message
    /// </summary>
    public struct RtpMidiBitrateReceiveLimit
    {
        public RtpMidiBitrateReceiveLimit(int ssrc, int bitrateLimit)
        {
            Ssrc = ssrc;
            BitrateLimit = bitrateLimit;
        }

        public int Ssrc { get; }
        public int BitrateLimit { get; }
    }

    /// <summary>
    /// RTP MIDI synchronization message
    /// </summary>
    public struct RtpMidiSynchronization
    {
        public RtpMidiSynchronization(int ssrc, byte count, long[] timestamps)
        {
            Ssrc = ssrc;
            Count = count;
            Timestamps = timestamps;
        }

        public int Ssrc { get; set; }

        public byte Count { get; set; }

        public long[] Timestamps { get; set; }
    }

    /// <summary>
    /// RTP MIDI receiver feedback message
    /// </summary>
    public struct RtpMidiReceiverFeedback
    {
        public RtpMidiReceiverFeedback(int ssrc, short sequenceNr)
        {
            Ssrc = ssrc;
            SequenceNr = sequenceNr;
        }

        public int Ssrc { get; set; }
        public short SequenceNr { get; set; }
    }

    /// <summary>
    /// RTP MIDI end session message
    /// </summary>
    public struct RtpMidiEndSession
    {
        public RtpMidiEndSession(int initiatorToken, int ssrc)
        {
            InitiatorToken = initiatorToken;
            Ssrc = ssrc;
        }

        public int InitiatorToken { get; }
        public int Ssrc { get; }
    }
}
