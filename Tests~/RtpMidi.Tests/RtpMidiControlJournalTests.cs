using System.Collections.Generic;
using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiControlJournalTests
    {
        [Fact]
        public void Volume_UsesValueTool_AndDecodesBack()
        {
            var encoded = Encode(12, (10, Cc(0, 7, 80)));
            var chapter = ChapterC(encoded);

            Assert.Equal(0x80, chapter[0]); // S=1, one log
            Assert.Equal(0x80 | 7, chapter[1]); // S=1, number 7
            Assert.Equal(80, chapter[2]); // A=0, value 80

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.ControlChange && c.Data1 == 7 && c.Data2 == 80);
        }

        [Fact]
        public void SustainOnOffOn_ToggleCountDetectsLostOff()
        {
            var encoded = Encode(
                14,
                (10, Cc(0, 64, 127)),
                (11, Cc(0, 64, 0)),
                (12, Cc(0, 64, 127)));
            var chapter = ChapterC(encoded);

            Assert.Equal(0x80 | 64, chapter[1]);
            Assert.Equal(0x80 | 3, chapter[2]); // A=1, T=0, ALT=3
        }

        [Fact]
        public void RpnTransaction_IsChapterM_NotChapterC()
        {
            var encoded = Encode(
                14,
                (10, Cc(0, 101, 0)),
                (11, Cc(0, 100, 1)),
                (12, Cc(0, 6, 40)));

            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocC);
            Assert.Equal(RtpMidiNoteJournal.TocM, ChannelToc(encoded) & RtpMidiNoteJournal.TocM);

            var chapter = ChapterM(encoded);
            Assert.Equal(0, chapter[0] & 0x40); // P=0
            Assert.Equal(0x20, chapter[0] & 0x20); // E=1
            Assert.Equal(1, chapter[2] & 0x7f); // PNUM-LSB
            Assert.Equal(0, chapter[3] & 0x7f); // PNUM-MSB, Q=0
            Assert.Equal(40, chapter[5] & 0x7f); // ENTRY-MSB
        }

        [Fact]
        public void PartialRpnMsb_SetsPendingAndDoesNotUseChapterC()
        {
            var encoded = Encode(12, (10, Cc(0, 101, 5)));
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocC);
            var chapter = ChapterM(encoded);
            Assert.Equal(0x40, chapter[0] & 0x40); // P=1
            Assert.Equal(0, chapter[0] & 0x20); // E=0
            Assert.Equal(5, chapter[2] & 0x7f);
            Assert.Equal(0, chapter[2] & 0x80); // Q=0
        }

        [Fact]
        public void NullParameter_CompletesWithoutAParameterLog()
        {
            var encoded = Encode(13, (10, Cc(0, 101, 0x7f)), (11, Cc(0, 100, 0x7f)));
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocC);
            var chapter = ChapterM(encoded);
            Assert.Equal(0, chapter[0] & 0x40);
            Assert.Equal(0, chapter[0] & 0x20);
            Assert.Equal(2, RtpMidiJournalSection.ReadLength10(chapter[0], chapter[1]));
        }

        [Fact]
        public void VolumeBeforeResetAllControllers_IsNotOmitted()
        {
            var encoded = Encode(13, (10, Cc(0, 7, 90)), (11, Cc(0, 121, 0)));
            var numbers = ControllerNumbers(ChapterC(encoded));
            Assert.Contains(7, numbers);
            Assert.Contains(121, numbers);
        }

        [Fact]
        public void ModulationBeforeResetAllControllers_IsOmitted()
        {
            var encoded = Encode(13, (10, Cc(0, 1, 20)), (11, Cc(0, 121, 0)));
            var numbers = ControllerNumbers(ChapterC(encoded));
            Assert.DoesNotContain(1, numbers);
            Assert.Contains(121, numbers);
        }

        [Fact]
        public void ExclusivePair_KeepsMostRecentOnly()
        {
            var encoded = Encode(13, (10, Cc(0, 124, 0)), (11, Cc(0, 125, 0)));
            var numbers = ControllerNumbers(ChapterC(encoded));
            Assert.DoesNotContain(124, numbers);
            Assert.Contains(125, numbers);
        }

        [Fact]
        public void FourteenBitLsb_OmittedWhenMsbIsMoreRecent()
        {
            var encoded = Encode(
                14,
                (10, Cc(0, 7, 100)),
                (11, Cc(0, 39, 10)),
                (12, Cc(0, 7, 90)));
            var numbers = ControllerNumbers(ChapterC(encoded));
            Assert.Contains(7, numbers);
            Assert.DoesNotContain(39, numbers);
            Assert.Equal(90, ChapterC(encoded)[2]);
        }

        [Fact]
        public void DataEntryOutsideTransaction_StaysInChapterC()
        {
            var encoded = Encode(12, (10, Cc(0, 6, 15)));
            var numbers = ControllerNumbers(ChapterC(encoded));
            Assert.Contains(6, numbers);
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocM);
        }

        [Fact]
        public void PreviousPacketVolume_ClearsS()
        {
            var encoded = Encode(11, (10, Cc(0, 7, 40)));
            Assert.Equal(0, encoded[0] & 0x80);
            Assert.Equal(0, ChapterC(encoded)[1] & 0x80);
        }

        private static byte[] Encode(ushort packetI, params (ushort seq, byte[] midi)[] items)
        {
            var journal = new RtpMidiJournal();
            ushort last = 0;
            var started = false;
            foreach (var item in items)
            {
                if (started && item.seq != last)
                {
                    journal.CommitPending(last);
                }

                journal.Record(item.midi);
                last = item.seq;
                started = true;
            }

            if (started)
            {
                journal.CommitPending(last);
            }

            return journal.Encode(packetI, items[0].seq);
        }

        private static byte[] Cc(int channel, int number, int value) =>
            new[] { (byte)(0xb0 | (channel & 0x0f)), (byte)number, (byte)value };

        private static int ChannelOffset(byte[] journal) => 3;

        private static byte ChannelToc(byte[] journal) => journal[ChannelOffset(journal) + 2];

        private static byte[] ChapterC(byte[] journal)
        {
            var start = ChannelOffset(journal) + 3;
            var logs = (journal[start] & 0x7f) + 1;
            var chapter = new byte[1 + (logs * 2)];
            System.Buffer.BlockCopy(journal, start, chapter, 0, chapter.Length);
            return chapter;
        }

        private static byte[] ChapterM(byte[] journal)
        {
            var start = ChannelOffset(journal) + 3;
            if ((ChannelToc(journal) & RtpMidiNoteJournal.TocC) == RtpMidiNoteJournal.TocC)
            {
                start += 1 + (((journal[start] & 0x7f) + 1) * 2);
            }

            var length = RtpMidiJournalSection.ReadLength10(journal[start], journal[start + 1]);
            var chapter = new byte[length];
            System.Buffer.BlockCopy(journal, start, chapter, 0, length);
            return chapter;
        }

        private static List<int> ControllerNumbers(byte[] chapter)
        {
            var numbers = new List<int>();
            var logs = (chapter[0] & 0x7f) + 1;
            for (var i = 0; i < logs; i++)
            {
                numbers.Add(chapter[1 + (i * 2)] & 0x7f);
            }

            return numbers;
        }
    }
}
