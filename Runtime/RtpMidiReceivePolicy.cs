namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Classification of an incoming RTP packet relative to previously received sequence numbers.
    /// </summary>
    public enum RtpPacketReceiveKind
    {
        /// <summary>First packet on the stream; treat as ending a loss (RFC 6295 §4).</summary>
        First,

        /// <summary>Exactly next expected sequence; no gap.</summary>
        InOrder,

        /// <summary>One or more packets missing before this one.</summary>
        Loss,

        /// <summary>Duplicate or behind the highest received; ignore for repair and MIDI.</summary>
        Reordered,
    }

    /// <summary>
    /// Receive-side recovery journal policy (RFC 6295 §4). Independent of chapter bodies.
    /// </summary>
    public static class RtpMidiReceivePolicy
    {
        /// <summary>
        /// Classifies <paramref name="incoming"/> given the highest sequence already accepted.
        /// </summary>
        public static RtpPacketReceiveKind Classify(bool isFirstPacket, ushort highestAccepted, ushort incoming)
        {
            if (isFirstPacket)
            {
                return RtpPacketReceiveKind.First;
            }

            var delta = RtpSequenceNumber.SerialDelta(highestAccepted, incoming);
            if (delta <= 0)
            {
                return RtpPacketReceiveKind.Reordered;
            }

            if (delta == 1)
            {
                return RtpPacketReceiveKind.InOrder;
            }

            return RtpPacketReceiveKind.Loss;
        }

        /// <summary>
        /// True when the journal may be applied (first packet or loss-ending packet).
        /// In-order and reordered packets must not apply the journal.
        /// </summary>
        public static bool ShouldApplyJournal(RtpPacketReceiveKind kind)
        {
            return kind == RtpPacketReceiveKind.First || kind == RtpPacketReceiveKind.Loss;
        }

        /// <summary>
        /// True when the MIDI command section should be delivered to the renderer.
        /// Reordered/duplicate packets are ignored to avoid indefinite artifacts (e.g. double NoteOn).
        /// </summary>
        public static bool ShouldPlayMidi(RtpPacketReceiveKind kind)
        {
            return kind != RtpPacketReceiveKind.Reordered;
        }

        /// <summary>
        /// Checkpoint covers the lost region when C ≤ (highest previously received + 1) (mod 2^16).
        /// </summary>
        public static bool CheckpointCoversLoss(ushort checkpointPacketSeqnum, ushort highestReceivedBeforePacket)
        {
            var coverLimit = (ushort)(highestReceivedBeforePacket + 1);
            return RtpSequenceNumber.SerialDelta(coverLimit, checkpointPacketSeqnum) <= 0;
        }
    }
}
