using System;
using System.Collections.Generic;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Send-side recovery journal. Record updates history only; Encode is read-only.
    /// Pending commands are assigned a packet sequence when that RTP packet is sent (after Encode for I).
    /// Checkpoint history is coded as Chapter N and Chapter E.
    /// </summary>
    public class RtpMidiJournal
    {
        private struct Entry
        {
            public ushort PacketSequence;
            public byte[] Midi;
        }

        private readonly List<Entry> committed = new List<Entry>();
        private readonly List<byte[]> pending = new List<byte[]>();
        private readonly int[,] noteRefCount = new int[16, 128];
        private bool hasSessionStart;
        private ushort sessionStartSequence;

        /// <summary>
        /// Pending plus committed command count. Encode must not change this.
        /// </summary>
        public int HistoryCount => committed.Count + pending.Count;

        /// <summary>
        /// Commands already assigned to sent packets.
        /// </summary>
        public int CommittedCount => committed.Count;

        /// <summary>
        /// Sequence of the first committed packet, if any.
        /// </summary>
        public bool TryGetSessionStartSequence(out ushort sequence)
        {
            sequence = sessionStartSequence;
            return hasSessionStart;
        }

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
            pending.Add(copy);
        }

        /// <summary>
        /// Encodes the recovery journal for RTP packet I with checkpoint C.
        /// Chapter N and E cover checkpoint history; empty history forces C = I.
        /// Does not mutate recorded history or note reference counts.
        /// </summary>
        public byte[] Encode(ushort packetSequenceI, ushort checkpointC)
        {
            if (!HasCommittedInRange(checkpointC, packetSequenceI))
            {
                checkpointC = packetSequenceI;
            }

            var history = new List<RtpMidiNoteJournal.HistoryItem>(committed.Count);
            for (var i = 0; i < committed.Count; i++)
            {
                history.Add(new RtpMidiNoteJournal.HistoryItem(committed[i].PacketSequence, committed[i].Midi));
            }

            return RtpMidiNoteJournal.Encode(history, noteRefCount, packetSequenceI, checkpointC);
        }

        /// <summary>
        /// Convenience for empty checkpoint history (C = I).
        /// </summary>
        public byte[] Encode(ushort packetSequenceI)
        {
            return Encode(packetSequenceI, packetSequenceI);
        }

        /// <summary>
        /// Assigns all pending commands to packet <paramref name="packetSequence"/> after that packet's journal is encoded.
        /// </summary>
        public void CommitPending(ushort packetSequence)
        {
            if (pending.Count == 0)
            {
                return;
            }

            if (!hasSessionStart)
            {
                hasSessionStart = true;
                sessionStartSequence = packetSequence;
            }

            foreach (var midi in pending)
            {
                committed.Add(new Entry { PacketSequence = packetSequence, Midi = midi });
                RtpMidiNoteJournal.ApplyCommitted(noteRefCount, midi);
            }

            pending.Clear();
        }

        /// <summary>
        /// Drops committed commands with packet sequence strictly before <paramref name="checkpointC"/>.
        /// </summary>
        public void PruneBefore(ushort checkpointC)
        {
            committed.RemoveAll(entry => RtpSequenceNumber.SerialDelta(entry.PacketSequence, checkpointC) > 0);
        }

        /// <summary>
        /// True when at least one committed command lies in checkpoint history [C, I).
        /// </summary>
        public bool HasCommittedInRange(ushort checkpointC, ushort packetSequenceI)
        {
            if (checkpointC == packetSequenceI)
            {
                return false;
            }

            for (var i = 0; i < committed.Count; i++)
            {
                if (IsInCheckpointHistory(committed[i].PacketSequence, checkpointC, packetSequenceI))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Number of committed commands in [C, I).
        /// </summary>
        public int CountCommittedInRange(ushort checkpointC, ushort packetSequenceI)
        {
            var count = 0;
            for (var i = 0; i < committed.Count; i++)
            {
                if (IsInCheckpointHistory(committed[i].PacketSequence, checkpointC, packetSequenceI))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// True when <paramref name="packetSequence"/> lies in checkpoint history [C, I).
        /// </summary>
        public static bool IsInCheckpointHistory(ushort packetSequence, ushort checkpointC, ushort packetSequenceI)
        {
            if (checkpointC == packetSequenceI)
            {
                return false;
            }

            // Forward walk from C to I: include seq if C <= seq < I (mod 2^16 serial).
            return RtpSequenceNumber.SerialDelta(checkpointC, packetSequence) >= 0
                   && RtpSequenceNumber.SerialDelta(packetSequence, packetSequenceI) > 0;
        }
    }
}
