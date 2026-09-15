namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Closed-loop checkpoint selection (RFC 6295 Appendix C.2.2.2) using AppleMIDI RS as feedback.
    /// </summary>
    public static class RtpMidiCheckpointPolicy
    {
        /// <summary>
        /// Chooses checkpoint packet seqnum C for outgoing packet I.
        /// Until the first RS, C stays at session start (complete journal). After RS, C = M(k)+1.
        /// Empty checkpoint history yields C = I.
        /// </summary>
        public static ushort SelectCheckpoint(
            ushort packetSequenceI,
            bool hasRemoteFeedback,
            uint remoteFeedbackExtended,
            bool hasSessionStart,
            ushort sessionStartSequence,
            RtpMidiJournal journal)
        {
            ushort candidate;
            if (!hasRemoteFeedback)
            {
                candidate = hasSessionStart ? sessionStartSequence : packetSequenceI;
            }
            else
            {
                candidate = RtpSequenceNumber.Low16(remoteFeedbackExtended + 1);
            }

            // C must not be serially ahead of I.
            if (RtpSequenceNumber.IsAheadOf(candidate, packetSequenceI))
            {
                candidate = packetSequenceI;
            }

            if (journal == null || !journal.HasCommittedInRange(candidate, packetSequenceI))
            {
                return packetSequenceI;
            }

            return candidate;
        }

        /// <summary>
        /// Extended M(k) from a 16-bit AppleMIDI RS value.
        /// </summary>
        public static uint ExtendReceiverFeedback(uint previousExtendedOrSendSeq, ushort feedbackSequence)
        {
            return RtpSequenceNumber.Extend(previousExtendedOrSendSeq, feedbackSequence);
        }

        /// <summary>
        /// Unacked span from M(k) to the last sent extended sequence (send - M).
        /// Without feedback, M is treated as sessionStart - 1.
        /// </summary>
        public static uint UnackedPacketSpan(
            uint lastSentExtended,
            bool hasRemoteFeedback,
            uint remoteFeedbackExtended,
            bool hasSessionStart,
            uint sessionStartExtended)
        {
            uint m;
            if (hasRemoteFeedback)
            {
                m = remoteFeedbackExtended;
            }
            else if (hasSessionStart)
            {
                m = sessionStartExtended - 1;
            }
            else
            {
                return 0;
            }

            if (lastSentExtended < m)
            {
                return 0;
            }

            return lastSentExtended - m;
        }
    }
}
