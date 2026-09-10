using System;
using System.Collections.Generic;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Send-side recovery journal. Phase 0 stores history but encodes an empty journal (C = I).
    /// Record updates history only; Encode is read-only.
    /// </summary>
    public class RtpMidiJournal
    {
        private readonly List<byte[]> sessionHistory = new List<byte[]>();

        /// <summary>
        /// Number of recorded send-side MIDI commands. Encode must not change this.
        /// </summary>
        public int HistoryCount => sessionHistory.Count;

        /// <summary>
        /// Records a MIDI command this participant is about to send. Does not encode.
        /// </summary>
        public void Record(byte[] midi)
        {
            if (midi == null || midi.Length == 0)
            {
                return;
            }

            var copy = new byte[midi.Length];
            Buffer.BlockCopy(midi, 0, copy, 0, midi.Length);
            sessionHistory.Add(copy);
        }

        /// <summary>
        /// Encodes the recovery journal for RTP packet I. Phase 0 always uses C = I (empty checkpoint history).
        /// Does not mutate recorded history.
        /// </summary>
        public byte[] Encode(ushort packetSequenceI)
        {
            return RtpMidiJournalSection.EncodeEmpty(packetSequenceI);
        }
    }
}
