using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiJournalPhase0Tests
    {
        [Fact]
        public void EmptyJournal_IsThreeOctets_WithSSetAndHClear()
        {
            var encoded = RtpMidiJournalSection.EncodeEmpty(0x1234);

            Assert.Equal(3, encoded.Length);
            Assert.Equal(RtpMidiJournalSection.FlagS, encoded[0]);
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagY);
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagA);
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagH);
            Assert.Equal(0, encoded[0] & 0x0f);
            Assert.Equal(0x1234, RtpMidiJournalSection.GetCheckpointPacketSeqnum(encoded));
        }

        [Fact]
        public void Encode_UsesCheckpointEqualToPacketI()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0x90, 60, 127 });

            var packetI = (ushort)0x00ab;
            var encoded = journal.Encode(packetI);

            Assert.Equal(packetI, RtpMidiJournalSection.GetCheckpointPacketSeqnum(encoded));
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagY);
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagA);
        }

        [Fact]
        public void Encode_DoesNotMutateRecordedHistory()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0x90, 60, 127 });
            journal.Record(new byte[] { 0x80, 60, 0 });
            Assert.Equal(2, journal.HistoryCount);

            var first = journal.Encode(10);
            var second = journal.Encode(10);

            Assert.Equal(2, journal.HistoryCount);
            Assert.Equal(first, second);
        }

        [Fact]
        public void Consume_EmptyJournal()
        {
            var journal = RtpMidiJournalSection.EncodeEmpty(1);
            var buffer = Concat(journal, new byte[] { 0xff });

            var result = RtpMidiJournalSection.TryConsume(buffer, 0, out var consumed);

            Assert.Equal(RtpMidiJournalSection.ConsumeResult.Ok, result);
            Assert.Equal(3, consumed);
        }

        [Fact]
        public void Consume_DoesNotStopAtSBitWhenSystemJournalPresent()
        {
            // S=1, Y=1, A=0, H=0. System LENGTH=4 (header + 2 payload octets).
            var journal = new byte[]
            {
                (byte)(RtpMidiJournalSection.FlagS | RtpMidiJournalSection.FlagY),
                0x00, 0x02,
                RtpMidiJournalSection.FlagS, 0x04, 0xaa, 0xbb,
            };

            var result = RtpMidiJournalSection.TryConsume(journal, 0, out var consumed);

            Assert.Equal(RtpMidiJournalSection.ConsumeResult.Ok, result);
            Assert.Equal(7, consumed);
        }

        [Fact]
        public void Consume_ChannelJournalsByHeaderInclusiveLength_InChannelOrder()
        {
            // A=1, TOTCHAN=1 → 2 channel journals. LENGTH=3 includes the 3-octet header (H=0).
            var journal = new byte[]
            {
                (byte)(RtpMidiJournalSection.FlagS | RtpMidiJournalSection.FlagA | 0x01),
                0x00, 0x01,
                // CHAN=0, H=0, LENGTH=3
                RtpMidiJournalSection.FlagS, 0x03, 0x00,
                // CHAN=2, H=0, LENGTH=3
                (byte)(RtpMidiJournalSection.FlagS | (2 << 3)), 0x03, 0x00,
                0xee,
            };

            var result = RtpMidiJournalSection.TryConsume(journal, 0, out var consumed);

            Assert.Equal(RtpMidiJournalSection.ConsumeResult.Ok, result);
            Assert.Equal(9, consumed);
            Assert.Equal(0, journal[3] & 0x04);
            Assert.Equal(0, journal[6] & 0x04);
            Assert.True(((journal[3] >> 3) & 0x0f) < ((journal[6] >> 3) & 0x0f));
        }

        [Fact]
        public void Consume_NotEnoughData()
        {
            var result = RtpMidiJournalSection.TryConsume(new byte[] { RtpMidiJournalSection.FlagS, 0x00 }, 0, out var consumed);

            Assert.Equal(RtpMidiJournalSection.ConsumeResult.NotEnoughData, result);
            Assert.Equal(0, consumed);
        }

        [Fact]
        public void Consume_InvalidSystemLength()
        {
            var journal = new byte[]
            {
                (byte)(RtpMidiJournalSection.FlagS | RtpMidiJournalSection.FlagY),
                0x00, 0x00,
                0x00, 0x01,
            };

            var result = RtpMidiJournalSection.TryConsume(journal, 0, out _);

            Assert.Equal(RtpMidiJournalSection.ConsumeResult.Invalid, result);
        }

        [Fact]
        public void SequenceNumber_ExtendsAcrossWrap()
        {
            uint previous = 0xfffe;
            var extended = RtpSequenceNumber.Extend(previous, 0x0001);

            Assert.Equal(0x10001u, extended);
            Assert.Equal((ushort)1, RtpSequenceNumber.Low16(extended));
        }

        [Fact]
        public void SequenceNumber_SerialDeltaTreatsWrapAsForward()
        {
            Assert.Equal(1, RtpSequenceNumber.SerialDelta(0xffff, 0x0000));
            Assert.True(RtpSequenceNumber.IsAheadOf(0x0002, 0xffff));
            Assert.False(RtpSequenceNumber.IsAheadOf(0xfffe, 0x0001));
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var result = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            return result;
        }
    }
}
