using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiSystemJournalTests
    {
        [Fact]
        public void Reset_EmitsChapterD_AndDecodesBack()
        {
            var encoded = Encode(12, 10, (10, new byte[] { 0xff }));
            Assert.Equal(RtpMidiJournalSection.FlagS | RtpMidiJournalSection.FlagY, encoded[0]);
            Assert.Equal(RtpMidiSystemJournal.TocD, SystemToc(encoded));
            var chapter = ChapterD(encoded);
            Assert.Equal(0x80 | 0x40, chapter[0]);
            Assert.Equal(0x80 | 1, chapter[1]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.SystemReset);
        }

        [Fact]
        public void ResetCount_IsSessionTotalModulo128()
        {
            var encoded = Encode(13, 10, (10, new byte[] { 0xff }), (11, new byte[] { 0xff }));
            Assert.Equal(0x80 | 2, ChapterD(encoded)[1]);
        }

        [Fact]
        public void Reset_AbsentWhenOutsideCheckpoint()
        {
            var encoded = Encode(13, 11, (10, new byte[] { 0xff }), (11, new byte[] { 0xfe }));
            Assert.Equal(0, SystemToc(encoded) & RtpMidiSystemJournal.TocD);
            Assert.Equal(RtpMidiSystemJournal.TocV, SystemToc(encoded) & RtpMidiSystemJournal.TocV);
        }

        [Fact]
        public void TuneRequestAndSongSelect_UseCountAndLatestValue()
        {
            var encoded = Encode(
                14,
                10,
                (10, new byte[] { 0xf6 }),
                (11, new byte[] { 0xf3, 1 }),
                (12, new byte[] { 0xf3, 4 }));
            var chapter = ChapterD(encoded);
            Assert.Equal(0x80 | 0x20 | 0x10, chapter[0]);
            Assert.Equal(0x80 | 1, chapter[1]);
            Assert.Equal(0x80 | 4, chapter[2]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.TuneRequest);
            Assert.Contains(commands, c => c.Type == MidiType.SongSelect && c.Data1 == 4);
        }

        [Fact]
        public void UndefinedCommands_AreEncodedAndSkippedOnDecode()
        {
            var encoded = Encode(14, 10, (10, new byte[] { 0xff }), (11, new byte[] { 0xf4 }), (12, new byte[] { 0xf9 }));
            var chapter = ChapterD(encoded);
            Assert.Equal(0x80 | 0x40 | 0x08 | 0x02, chapter[0]);
            Assert.Equal(0xc0, chapter[2]);
            Assert.Equal(3, chapter[3]);
            Assert.Equal(1, chapter[4]);
            Assert.Equal(0xc2, chapter[5]);
            Assert.Equal(1, chapter[6]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.SystemReset);
        }

        [Fact]
        public void ActiveSense_EmitsChapterV_AndClearsSWhenPrevious()
        {
            var encoded = Encode(11, 10, (10, new byte[] { 0xfe }));
            Assert.Equal(0, encoded[0] & 0x80);
            Assert.Equal(RtpMidiSystemJournal.TocV, SystemToc(encoded));
            Assert.Equal(1, ChapterV(encoded) & 0x7f);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.ActiveSensing);
        }

        [Fact]
        public void ActiveSenseBeforeReset_IsNotActive()
        {
            var encoded = Encode(13, 10, (10, new byte[] { 0xfe }), (11, new byte[] { 0xff }));
            Assert.Equal(0, SystemToc(encoded) & RtpMidiSystemJournal.TocV);
            Assert.Equal(RtpMidiSystemJournal.TocD, SystemToc(encoded) & RtpMidiSystemJournal.TocD);
        }

        [Fact]
        public void StartAndClock_EmitChapterQ()
        {
            var encoded = Encode(13, 10, (10, new byte[] { 0xfa }), (11, new byte[] { 0xf8 }));
            Assert.Equal(RtpMidiSystemJournal.TocQ, SystemToc(encoded));
            var chapter = ChapterQ(encoded);
            Assert.Equal(0x80 | 0x40 | 0x20, Assert.Single(chapter));

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.Start);
        }

        [Fact]
        public void StartThenStop_DoesNotChangeChapterQ()
        {
            var encoded = Encode(13, 10, (10, new byte[] { 0xfa }), (11, new byte[] { 0xfc }));
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagY);
        }

        [Fact]
        public void SongPosition_CodesClocksAndStop()
        {
            var encoded = Encode(12, 10, (10, new byte[] { 0xf2, 2, 0 }));
            var chapter = ChapterQ(encoded);
            Assert.Equal(0x80 | 0x10, chapter[0]);
            Assert.Equal(0x00, chapter[1]);
            Assert.Equal(12, chapter[2]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.SongPosition && c.Data1 == 2);
            Assert.Contains(commands, c => c.Type == MidiType.Stop);
        }

        [Fact]
        public void QuarterFrame_EmitsPartialChapterF()
        {
            var encoded = Encode(12, 10, (10, new byte[] { 0xf1, 0x03 }));
            Assert.Equal(RtpMidiSystemJournal.TocF, SystemToc(encoded));
            var chapter = ChapterF(encoded);
            Assert.Equal(0x80 | 0x20, chapter[0]);
            Assert.Equal(0x30, chapter[1]);
        }

        [Fact]
        public void ForwardQuarterFrames_EmitCompleteWithTwoFrameOffset()
        {
            var items = new (ushort seq, byte[] midi)[8];
            for (var type = 0; type < 8; type++)
            {
                items[type] = ((ushort)(10 + type), new byte[] { 0xf1, (byte)(type << 4) });
            }

            var encoded = Encode(19, 10, items);
            var chapter = ChapterF(encoded);
            Assert.Equal(0x80 | 0x40 | 0x10 | 0x07, chapter[0]);
            Assert.Equal(0x20, chapter[1]);
            Assert.Equal(0, chapter[2]);
        }

        [Fact]
        public void OrdinarySysEx_MustNotEmitChapterF()
        {
            var encoded = Encode(12, 10, (10, new byte[] { 0xf0, 0x41, 0x10, 0xf7 }));
            Assert.Equal(0, SystemToc(encoded) & RtpMidiSystemJournal.TocF);
            Assert.Equal(RtpMidiSystemJournal.TocX, SystemToc(encoded) & RtpMidiSystemJournal.TocX);
        }

        [Fact]
        public void FullFrame_IsChapterF_NotChapterX()
        {
            var encoded = Encode(12, 10, (10, new byte[] { 0xf0, 0x7f, 0x7f, 0x01, 0x01, 1, 2, 3, 4, 0xf7 }));
            Assert.Equal(RtpMidiSystemJournal.TocF, SystemToc(encoded));
            var chapter = ChapterF(encoded);
            Assert.Equal(0x80 | 0x40 | 0x07, chapter[0]);
            Assert.Equal(1, chapter[1]);
            Assert.Equal(4, chapter[4]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.SystemExclusive && c.Payload != null && c.Payload[5] == 1);
        }

        [Fact]
        public void FinishedSysEx_EmitsChapterX_AndDecodesBack()
        {
            var encoded = Encode(12, 10, (10, new byte[] { 0xf0, 0x41, 0x10, 0xf7 }));
            var chapter = ChapterX(encoded);
            Assert.Equal(0xab, chapter[0]);
            Assert.Equal(1, chapter[1]);
            Assert.Equal(0x41, chapter[2]);
            Assert.Equal(0x80 | 0x10, chapter[3]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            var sysex = Assert.Single(commands, c => c.Type == MidiType.SystemExclusive);
            Assert.Equal(new byte[] { 0xf0, 0x41, 0x10, 0xf7 }, sysex.Payload);
        }

        [Fact]
        public void SegmentedSysEx_IsUnfinishedUntilLast()
        {
            var unfinished = Encode(12, 10, (10, new byte[] { 0xf0, 0x11, 0xf0 }));
            Assert.Equal(0, ChapterX(unfinished)[0] & 0x03);

            var finished = Encode(
                13,
                10,
                (10, new byte[] { 0xf0, 0x11, 0xf0 }),
                (11, new byte[] { 0xf7, 0x22, 0xf7 }));
            var chapter = ChapterX(finished);
            Assert.Equal(3, chapter[0] & 0x03);
            Assert.Equal(0x11, chapter[2]);
            Assert.Equal(0x80 | 0x22, chapter[3]);
        }

        [Fact]
        public void CancelledSysEx_HasNoDataField()
        {
            var encoded = Encode(13, 10, (10, new byte[] { 0xf0, 0x11, 0xf0 }), (11, new byte[] { 0xf7, 0xf4 }));
            var chapter = ChapterX(encoded);
            Assert.Equal(1, chapter[0] & 0x03);
            Assert.Equal(0, chapter[0] & 0x08);
            Assert.Equal(2, chapter.Length);
        }

        [Fact]
        public void ResetStateSysEx_DropsEarlierSongSelect_AndIsChapterX()
        {
            var encoded = Encode(
                13,
                10,
                (10, new byte[] { 0xf3, 2 }),
                (11, new byte[] { 0xf0, 0x7e, 0x7f, 0x09, 0x01, 0xf7 }));
            Assert.Equal(0, SystemToc(encoded) & RtpMidiSystemJournal.TocD);
            Assert.Equal(RtpMidiSystemJournal.TocX, SystemToc(encoded) & RtpMidiSystemJournal.TocX);
        }

        private static byte[] Encode(ushort packetI, ushort checkpoint, params (ushort seq, byte[] midi)[] items)
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

            return journal.Encode(packetI, checkpoint);
        }

        private static byte SystemToc(byte[] journal) => (byte)(journal[3] & 0x7c);

        private static int SystemBody(byte[] journal) => 5;

        private static byte[] ChapterD(byte[] journal)
        {
            Assert.Equal(RtpMidiSystemJournal.TocD, SystemToc(journal) & RtpMidiSystemJournal.TocD);
            return SliceUntil(journal, SystemBody(journal), NextAfterD);
        }

        private static byte ChapterV(byte[] journal) => journal[SkipTo(journal, RtpMidiSystemJournal.TocV)];

        private static byte[] ChapterQ(byte[] journal)
        {
            var start = SkipTo(journal, RtpMidiSystemJournal.TocQ);
            var length = 1;
            if ((journal[start] & 0x10) == 0x10)
            {
                length += 2;
            }

            if ((journal[start] & 0x04) == 0x04)
            {
                length += 3;
            }

            return Copy(journal, start, length);
        }

        private static byte[] ChapterF(byte[] journal)
        {
            var start = SkipTo(journal, RtpMidiSystemJournal.TocF);
            var length = 1;
            if ((journal[start] & 0x40) == 0x40)
            {
                length += 4;
            }

            if ((journal[start] & 0x20) == 0x20)
            {
                length += 4;
            }

            return Copy(journal, start, length);
        }

        private static byte[] ChapterX(byte[] journal)
        {
            var start = SkipTo(journal, RtpMidiSystemJournal.TocX);
            var end = 3 + RtpMidiJournalSection.ReadLength10(journal[3], journal[4]);
            return Copy(journal, start, end - start);
        }

        private static int SkipTo(byte[] journal, byte chapter)
        {
            var offset = SystemBody(journal);
            var toc = SystemToc(journal);
            if ((toc & RtpMidiSystemJournal.TocD) == RtpMidiSystemJournal.TocD && chapter != RtpMidiSystemJournal.TocD)
            {
                offset = NextAfterD(journal, offset);
            }

            if ((toc & RtpMidiSystemJournal.TocV) == RtpMidiSystemJournal.TocV &&
                chapter != RtpMidiSystemJournal.TocD &&
                chapter != RtpMidiSystemJournal.TocV)
            {
                offset += 1;
            }

            if ((toc & RtpMidiSystemJournal.TocQ) == RtpMidiSystemJournal.TocQ &&
                (chapter == RtpMidiSystemJournal.TocF || chapter == RtpMidiSystemJournal.TocX))
            {
                offset += 1 + (((journal[offset] & 0x10) == 0x10) ? 2 : 0);
            }

            if ((toc & RtpMidiSystemJournal.TocF) == RtpMidiSystemJournal.TocF && chapter == RtpMidiSystemJournal.TocX)
            {
                offset += 1 + (((journal[offset] & 0x40) == 0x40) ? 4 : 0) + (((journal[offset] & 0x20) == 0x20) ? 4 : 0);
            }

            return offset;
        }

        private static int NextAfterD(byte[] journal, int offset)
        {
            var flags = journal[offset];
            var cursor = offset + 1;
            if ((flags & 0x40) == 0x40)
            {
                cursor++;
            }

            if ((flags & 0x20) == 0x20)
            {
                cursor++;
            }

            if ((flags & 0x10) == 0x10)
            {
                cursor++;
            }

            if ((flags & 0x08) == 0x08)
            {
                cursor += RtpMidiJournalSection.ReadLength10(journal[cursor], journal[cursor + 1]);
            }

            if ((flags & 0x04) == 0x04)
            {
                cursor += RtpMidiJournalSection.ReadLength10(journal[cursor], journal[cursor + 1]);
            }

            if ((flags & 0x02) == 0x02)
            {
                cursor += journal[cursor] & 0x1f;
            }

            if ((flags & 0x01) == 0x01)
            {
                cursor += journal[cursor] & 0x1f;
            }

            return cursor;
        }

        private static byte[] SliceUntil(byte[] journal, int start, System.Func<byte[], int, int> endAt)
        {
            var end = endAt(journal, start);
            return Copy(journal, start, end - start);
        }

        private static byte[] Copy(byte[] journal, int start, int length)
        {
            var chapter = new byte[length];
            System.Buffer.BlockCopy(journal, start, chapter, 0, length);
            return chapter;
        }
    }
}
