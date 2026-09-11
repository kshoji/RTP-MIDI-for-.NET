using System.Linq;
using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiJournalPhase8Tests
    {
        [Fact]
        public void PrunePastBank_KeepsBankInChapterP()
        {
            var journal = new RtpMidiJournal();
            journal.Record(Cc(0, 0, 6));
            journal.CommitPending(10);
            journal.Record(Program(0, 9));
            journal.CommitPending(11);

            journal.PruneBefore(11);
            Assert.Equal(1, journal.CommittedCount);

            var encoded = journal.Encode(13, 11);
            var chapter = ChapterP(encoded);
            Assert.Equal(9, chapter[0] & 0x7f);
            Assert.Equal(0x80 | 6, chapter[1]);
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocC);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.ControlChange && c.Data1 == 0 && c.Data2 == 6);
            Assert.Contains(commands, c => c.Type == MidiType.ProgramChange && c.Data1 == 9);
        }

        [Fact]
        public void PrunePastBankMsbAndLsb_KeepsBothInChapterP()
        {
            var journal = new RtpMidiJournal();
            journal.Record(Cc(0, 0, 3));
            journal.CommitPending(10);
            journal.Record(Cc(0, 32, 5));
            journal.CommitPending(11);
            journal.Record(Program(0, 20));
            journal.CommitPending(12);

            journal.PruneBefore(12);
            Assert.Equal(1, journal.CommittedCount);

            var chapter = ChapterP(journal.Encode(14, 12));
            Assert.Equal(20, chapter[0] & 0x7f);
            Assert.Equal(0x80 | 3, chapter[1]);
            Assert.Equal(5, chapter[2]);
        }

        [Fact]
        public void PrunePastBankAndCc121_KeepsXInChapterP()
        {
            var journal = new RtpMidiJournal();
            journal.Record(Cc(0, 0, 2));
            journal.CommitPending(10);
            journal.Record(Cc(0, 121, 0));
            journal.CommitPending(11);
            journal.Record(Program(0, 4));
            journal.CommitPending(12);

            journal.PruneBefore(12);
            Assert.Equal(1, journal.CommittedCount);

            var chapter = ChapterP(journal.Encode(14, 12));
            Assert.Equal(4, chapter[0] & 0x7f);
            Assert.Equal(0x80 | 2, chapter[1]);
            Assert.Equal(0x80, chapter[2] & 0x80);
        }

        [Fact]
        public void PrunePastProgram_LaterProgramStillUsesSessionBank()
        {
            var journal = new RtpMidiJournal();
            journal.Record(Cc(0, 0, 6));
            journal.CommitPending(10);
            journal.Record(Program(0, 9));
            journal.CommitPending(11);
            journal.PruneBefore(12);
            Assert.Equal(0, journal.CommittedCount);

            journal.Record(Program(0, 10));
            journal.CommitPending(12);

            var chapter = ChapterP(journal.Encode(14, 12));
            Assert.Equal(10, chapter[0] & 0x7f);
            Assert.Equal(0x80 | 6, chapter[1]);
        }

        [Fact]
        public void PrunePastRpnNumber_KeepsEOnDataEntry()
        {
            var journal = new RtpMidiJournal();
            journal.Record(Cc(0, 101, 0));
            journal.CommitPending(10);
            journal.Record(Cc(0, 100, 1));
            journal.CommitPending(11);
            journal.Record(Cc(0, 6, 40));
            journal.CommitPending(12);

            journal.PruneBefore(12);
            Assert.Equal(1, journal.CommittedCount);

            var encoded = journal.Encode(14, 12);
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocC);
            Assert.Equal(RtpMidiNoteJournal.TocM, ChannelToc(encoded) & RtpMidiNoteJournal.TocM);

            var chapter = ChapterM(encoded);
            Assert.Equal(0, chapter[0] & 0x40); // P=0: MSB is not in checkpoint
            Assert.Equal(0x20, chapter[0] & 0x20); // E=1
            Assert.Equal(1, chapter[2] & 0x7f); // PNUM-LSB
            Assert.Equal(0, chapter[3] & 0x7f); // PNUM-MSB
            Assert.Equal(40, chapter[5] & 0x7f); // ENTRY-MSB

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Equal(new[] { 101, 100, 6 }, commands.Where(c => c.Type == MidiType.ControlChange).Select(c => c.Data1));
            Assert.Equal(40, commands.Last(c => c.Data1 == 6).Data2);
        }

        private static byte[] Program(int channel, int program) =>
            new[] { (byte)(0xc0 | (channel & 0x0f)), (byte)program };

        private static byte[] Cc(int channel, int number, int value) =>
            new[] { (byte)(0xb0 | (channel & 0x0f)), (byte)number, (byte)value };

        private static byte ChannelToc(byte[] journal) => journal[5];

        private static byte[] ChapterP(byte[] journal)
        {
            Assert.Equal(RtpMidiNoteJournal.TocP, ChannelToc(journal) & RtpMidiNoteJournal.TocP);
            return new[] { journal[6], journal[7], journal[8] };
        }

        private static byte[] ChapterM(byte[] journal)
        {
            var start = 6;
            if ((ChannelToc(journal) & RtpMidiNoteJournal.TocC) == RtpMidiNoteJournal.TocC)
            {
                start += 1 + (((journal[start] & 0x7f) + 1) * 2);
            }

            var length = RtpMidiJournalSection.ReadLength10(journal[start], journal[start + 1]);
            var chapter = new byte[length];
            System.Buffer.BlockCopy(journal, start, chapter, 0, length);
            return chapter;
        }
    }
}
