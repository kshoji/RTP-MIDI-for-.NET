using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiJournalPhase2Tests
    {
        [Fact]
        public void EmptyHistory_UsesCheckpointEqualToPacketI()
        {
            var journal = new RtpMidiJournal();
            var encoded = journal.Encode(0x0100, 0x0001);

            Assert.Equal((ushort)0x0100, RtpMidiJournalSection.GetCheckpointPacketSeqnum(encoded));
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagY);
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagA);
        }

        [Fact]
        public void BeforeFirstRs_CheckpointStaysAtSessionStart_CompleteJournal()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0x90, 60, 127 });
            journal.CommitPending(10);
            journal.Record(new byte[] { 0x80, 60, 0 });
            journal.CommitPending(11);

            var c = RtpMidiCheckpointPolicy.SelectCheckpoint(
                packetSequenceI: 12,
                hasRemoteFeedback: false,
                remoteFeedbackExtended: 0,
                hasSessionStart: true,
                sessionStartSequence: 10,
                journal: journal);

            Assert.Equal((ushort)10, c);
            Assert.Equal(2, journal.CountCommittedInRange(c, 12));

            var encoded = journal.Encode(12, c);
            Assert.Equal((ushort)10, RtpMidiJournalSection.GetCheckpointPacketSeqnum(encoded));
        }

        [Fact]
        public void AfterRs_CheckpointAdvancesToMkPlusOne()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0x90, 60, 127 });
            journal.CommitPending(10);
            journal.Record(new byte[] { 0x80, 60, 0 });
            journal.CommitPending(11);
            journal.Record(new byte[] { 0xb0, 7, 100 });
            journal.CommitPending(12);

            var c = RtpMidiCheckpointPolicy.SelectCheckpoint(
                packetSequenceI: 13,
                hasRemoteFeedback: true,
                remoteFeedbackExtended: 11,
                hasSessionStart: true,
                sessionStartSequence: 10,
                journal: journal);

            Assert.Equal((ushort)12, c);
            Assert.Equal(1, journal.CountCommittedInRange(c, 13));
        }

        [Fact]
        public void WhenRsCatchesUp_JournalShrinksToEmptyCheckpoint()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0x90, 60, 127 });
            journal.CommitPending(10);
            journal.Record(new byte[] { 0x80, 60, 0 });
            journal.CommitPending(11);

            var c = RtpMidiCheckpointPolicy.SelectCheckpoint(
                packetSequenceI: 12,
                hasRemoteFeedback: true,
                remoteFeedbackExtended: 11,
                hasSessionStart: true,
                sessionStartSequence: 10,
                journal: journal);

            Assert.Equal((ushort)12, c);
            var encoded = journal.Encode(12, c);
            Assert.Equal((ushort)12, RtpMidiJournalSection.GetCheckpointPacketSeqnum(encoded));

            journal.PruneBefore(c);
            Assert.Equal(0, journal.CommittedCount);
        }

        [Fact]
        public void CheckpointSelection_WrapAround()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0x90, 60, 127 });
            journal.CommitPending(0xfffe);
            journal.Record(new byte[] { 0x80, 60, 0 });
            journal.CommitPending(0xffff);
            journal.Record(new byte[] { 0xb0, 7, 1 });
            journal.CommitPending(0);

            var c = RtpMidiCheckpointPolicy.SelectCheckpoint(
                packetSequenceI: 1,
                hasRemoteFeedback: true,
                remoteFeedbackExtended: 0xffff,
                hasSessionStart: true,
                sessionStartSequence: 0xfffe,
                journal: journal);

            Assert.Equal((ushort)0, c);
            Assert.Equal(1, journal.CountCommittedInRange(c, 1));
            Assert.True(RtpMidiJournal.IsInCheckpointHistory(0, c, 1));
            Assert.False(RtpMidiJournal.IsInCheckpointHistory(0xffff, c, 1));
        }

        [Fact]
        public void ExtendReceiverFeedback_PreservesWrapCountFromSendSeq()
        {
            uint sendExtended = 0x10005;
            var extended = RtpMidiCheckpointPolicy.ExtendReceiverFeedback(sendExtended, 0x0003);
            Assert.Equal(0x10003u, extended);
            Assert.Equal((ushort)3, RtpSequenceNumber.Low16(extended));
        }

        [Fact]
        public void UnackedPacketSpan_GrowsWithoutRs_ThenShrinksWithRs()
        {
            var withoutRs = RtpMidiCheckpointPolicy.UnackedPacketSpan(
                lastSentExtended: 100,
                hasRemoteFeedback: false,
                remoteFeedbackExtended: 0,
                hasSessionStart: true,
                sessionStartExtended: 10);
            Assert.Equal(91u, withoutRs);

            var withRs = RtpMidiCheckpointPolicy.UnackedPacketSpan(
                lastSentExtended: 100,
                hasRemoteFeedback: true,
                remoteFeedbackExtended: 95,
                hasSessionStart: true,
                sessionStartExtended: 10);
            Assert.Equal(5u, withRs);
        }

        [Fact]
        public void Encode_DoesNotMutateHistory_CommitAndPruneAreExplicit()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0x90, 60, 127 });
            Assert.Equal(1, journal.HistoryCount);
            Assert.Equal(0, journal.CommittedCount);

            var encoded = journal.Encode(5, 5);
            Assert.Equal(1, journal.HistoryCount);
            Assert.Equal(3, encoded.Length);

            journal.CommitPending(5);
            Assert.Equal(1, journal.CommittedCount);

            journal.PruneBefore(6);
            Assert.Equal(0, journal.CommittedCount);
        }

        [Fact]
        public void CurrentPacketMidi_NotIncludedInCheckpointHistory()
        {
            var journal = new RtpMidiJournal();
            journal.Record(new byte[] { 0x90, 60, 127 });
            journal.CommitPending(10);

            // Pending for packet 11 must not appear in journal for I=11.
            journal.Record(new byte[] { 0x80, 60, 0 });
            Assert.False(journal.HasCommittedInRange(10, 11) && journal.CountCommittedInRange(10, 11) == 2);
            Assert.Equal(1, journal.CountCommittedInRange(10, 11));

            var c = RtpMidiCheckpointPolicy.SelectCheckpoint(11, false, 0, true, 10, journal);
            Assert.Equal((ushort)10, c);
            journal.Encode(11, c);
            journal.CommitPending(11);
            Assert.Equal(2, journal.CommittedCount);
        }
    }
}
