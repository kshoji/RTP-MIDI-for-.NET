using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiPayloadBudgetTests
    {
        [Fact]
        public void EmptyMidiPacket_FitsJournalUpToUdpLimit()
        {
            var maxJournal = RtpMidiParticipant.MaxUdpPayloadSize - RtpMidiPayloadBudget.RtpHeaderLength - 1;
            Assert.Equal(1187, maxJournal);
            Assert.True(RtpMidiPayloadBudget.Fits(0, maxJournal));
            Assert.False(RtpMidiPayloadBudget.Fits(0, maxJournal + 1));
            Assert.Equal(RtpMidiPayloadBudget.Layout.Combined, RtpMidiPayloadBudget.Choose(0, maxJournal));
            Assert.Equal(RtpMidiPayloadBudget.Layout.Overflow, RtpMidiPayloadBudget.Choose(0, maxJournal + 1));
        }

        [Fact]
        public void MidiSectionAndJournal_DeferWhenTogetherTheyExceedMtu()
        {
            var midi = RtpMidiParticipant.MaxBufferSize;
            var withMidi = RtpMidiParticipant.MaxUdpPayloadSize -
                           RtpMidiPayloadBudget.UdpPayloadLength(midi, 0);
            var emptyMidiMax = RtpMidiParticipant.MaxUdpPayloadSize -
                               RtpMidiPayloadBudget.RtpHeaderLength - 1;
            Assert.True(withMidi < emptyMidiMax);

            Assert.Equal(RtpMidiPayloadBudget.Layout.Combined, RtpMidiPayloadBudget.Choose(midi, withMidi));
            Assert.Equal(
                RtpMidiPayloadBudget.Layout.MidiThenJournal,
                RtpMidiPayloadBudget.Choose(midi, withMidi + 1));
            Assert.True(RtpMidiPayloadBudget.Fits(midi, RtpMidiJournalSection.EncodeEmpty(11).Length));
            Assert.True(RtpMidiPayloadBudget.Fits(0, withMidi + 1));
        }

        [Fact]
        public void DeferredMidiPacket_UsesEmptyJournalWithCheckpointEqualToI()
        {
            var empty = RtpMidiJournalSection.EncodeEmpty(0x0042);
            Assert.Equal(3, empty.Length);
            Assert.Equal(0x0042, RtpMidiJournalSection.GetCheckpointPacketSeqnum(empty));
            Assert.Equal(0, empty[0] & RtpMidiJournalSection.FlagY);
            Assert.Equal(0, empty[0] & RtpMidiJournalSection.FlagA);
            Assert.True(RtpMidiPayloadBudget.Fits(RtpMidiParticipant.MaxBufferSize, empty.Length));
        }

        [Fact]
        public void JournalLargerThanUdpPayload_IsOverflowAndWouldDisconnect()
        {
            var encoded = EncodeHugeSysExJournal();
            Assert.False(RtpMidiPayloadBudget.Fits(0, encoded.Length));
            Assert.Equal(RtpMidiPayloadBudget.Layout.Overflow, RtpMidiPayloadBudget.Choose(0, encoded.Length));
            Assert.Equal(
                RtpMidiPayloadBudget.Layout.Overflow,
                RtpMidiPayloadBudget.Choose(RtpMidiParticipant.MaxBufferSize, encoded.Length));
        }

        [Fact]
        public void MidiThenJournal_KeepsFullHistoryForTheFollowUpPacket()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0xb0, 7, 80 });
            journal.CommitPending(10);

            var midiFirstJournal = RtpMidiJournalSection.EncodeEmpty(11);
            Assert.Equal((ushort)11, RtpMidiJournalSection.GetCheckpointPacketSeqnum(midiFirstJournal));
            Assert.Equal(1, journal.CommittedCount);

            journal.Record(new byte[] { 0xb0, 7, 90 });
            journal.CommitPending(11);
            Assert.Equal(2, journal.CommittedCount);

            var followUp = journal.Encode(12, 10);
            Assert.True(RtpMidiNoteJournal.TryDecode(followUp, followUp.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.ControlChange && c.Data1 == 7 && c.Data2 == 90);
        }

        private static byte[] EncodeHugeSysExJournal()
        {
            var sysex = new byte[1600];
            sysex[0] = 0xf0;
            sysex[1] = 0x7d;
            for (var i = 2; i < sysex.Length - 1; i++)
            {
                sysex[i] = (byte)(i & 0x7f);
            }

            sysex[sysex.Length - 1] = 0xf7;
            var journal = new RtpMidiJournal();
            journal.Record(sysex);
            journal.CommitPending(10);
            return journal.Encode(11, 10);
        }
    }
}
