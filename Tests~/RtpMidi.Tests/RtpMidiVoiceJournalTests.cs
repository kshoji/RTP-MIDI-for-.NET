using System.Collections.Generic;
using System.Linq;
using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiVoiceJournalTests
    {
        [Fact]
        public void ProgramChange_EmitsChapterP_AndDecodesBack()
        {
            var encoded = Encode(12, 10, (10, Program(0, 12)));
            Assert.Equal(RtpMidiNoteJournal.TocP, ChannelToc(encoded));
            var chapter = ChapterP(encoded);
            Assert.Equal(3, chapter.Length);
            Assert.Equal(0x80 | 12, chapter[0]);
            Assert.Equal(0, chapter[1]);
            Assert.Equal(0, chapter[2]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.ProgramChange && c.Data1 == 12);
            Assert.DoesNotContain(commands, c => c.Type == MidiType.ControlChange);
        }

        [Fact]
        public void ProgramChange_AbsentWhenOutsideCheckpoint()
        {
            var encoded = Encode(13, 11, (10, Program(0, 4)), (11, Pitch(0, 10, 64)));
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocP);
            Assert.Equal(RtpMidiNoteJournal.TocW, ChannelToc(encoded) & RtpMidiNoteJournal.TocW);
        }

        [Fact]
        public void MostRecentProgram_IsCoded()
        {
            var encoded = Encode(13, 10, (10, Program(0, 1)), (11, Program(0, 2)));
            Assert.Equal(2, ChapterP(encoded)[0] & 0x7f);
        }

        [Fact]
        public void BankSelect_CodedInChapterP_AndOmittedFromChapterC()
        {
            var encoded = Encode(
                14,
                10,
                (10, Cc(0, 0, 3)),
                (11, Cc(0, 32, 5)),
                (12, Program(0, 20)));
            Assert.Equal(RtpMidiNoteJournal.TocP, ChannelToc(encoded) & RtpMidiNoteJournal.TocP);
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocC);

            var chapter = ChapterP(encoded);
            Assert.Equal(0x80 | 20, chapter[0]);
            Assert.Equal(0x80 | 3, chapter[1]);
            Assert.Equal(5, chapter[2]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Equal(new[] { 0, 32 }, commands.Where(c => c.Type == MidiType.ControlChange).Select(c => c.Data1));
            Assert.Equal(3, commands[0].Data2);
            Assert.Equal(5, commands[1].Data2);
            Assert.Equal(MidiType.ProgramChange, commands[2].Type);
            Assert.Equal(20, commands[2].Data1);
        }

        [Fact]
        public void BankBeforeCheckpoint_StillCodedWithLaterProgram()
        {
            var encoded = Encode(13, 11, (10, Cc(0, 0, 6)), (11, Program(0, 9)));
            var chapter = ChapterP(encoded);
            Assert.Equal(9, chapter[0] & 0x7f);
            Assert.Equal(0x80 | 6, chapter[1]);
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocC);
        }

        [Fact]
        public void BankAfterProgram_StaysInChapterC()
        {
            var encoded = Encode(13, 10, (10, Program(0, 10)), (11, Cc(0, 0, 8)));
            Assert.Equal(0, ChapterP(encoded)[1] & 0x80);
            var numbers = ControllerNumbers(ChapterC(encoded));
            Assert.Contains(0, numbers);
            Assert.Equal(8, ControllerValue(encoded, 0));
        }

        [Fact]
        public void EarlierBankLsb_IsNotCodedWhenMsbFollows()
        {
            var encoded = Encode(
                15,
                10,
                (10, Cc(0, 32, 9)),
                (11, Cc(0, 0, 1)),
                (12, Cc(0, 32, 4)),
                (13, Program(0, 2)));
            var chapter = ChapterP(encoded);
            Assert.Equal(0x80 | 1, chapter[1]);
            Assert.Equal(4, chapter[2]);
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocC);
        }

        [Fact]
        public void Cc121BetweenBankAndProgram_SetsX_AndDoesNotOmitVolume()
        {
            var encoded = Encode(
                15,
                10,
                (10, Cc(0, 7, 90)),
                (11, Cc(0, 0, 2)),
                (12, Cc(0, 121, 0)),
                (13, Program(0, 4)));
            var chapter = ChapterP(encoded);
            Assert.Equal(0x80 | 2, chapter[1]);
            Assert.Equal(0x80, chapter[2]);
            var numbers = ControllerNumbers(ChapterC(encoded));
            Assert.DoesNotContain(0, numbers);
            Assert.Contains(7, numbers);
            Assert.Contains(121, numbers);
        }

        [Fact]
        public void PitchBend_EmitsChapterW_AndDecodesBack()
        {
            var encoded = Encode(12, 10, (10, Pitch(0, 10, 64)));
            Assert.Equal(RtpMidiNoteJournal.TocW, ChannelToc(encoded));
            var chapter = ChapterW(encoded);
            Assert.Equal(0x80 | 10, chapter[0]);
            Assert.Equal(64, chapter[1]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.PitchBend && c.Data1 == 10 && c.Data2 == 64);
        }

        [Fact]
        public void PitchBend_AbsentAfterResetAllControllers()
        {
            var encoded = Encode(13, 10, (10, Pitch(0, 20, 40)), (11, Cc(0, 121, 0)));
            Assert.Equal(0, ChannelToc(encoded) & RtpMidiNoteJournal.TocW);
            Assert.Contains(121, ControllerNumbers(ChapterC(encoded)));
        }

        [Fact]
        public void ChannelAftertouch_EmitsChapterT_AndDecodesBack()
        {
            var encoded = Encode(12, 10, (10, ChannelPressure(0, 70)));
            Assert.Equal(RtpMidiNoteJournal.TocT, ChannelToc(encoded));
            Assert.Equal(0x80 | 70, ChapterT(encoded));

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Contains(commands, c => c.Type == MidiType.AfterTouchChannel && c.Data1 == 70);
        }

        [Fact]
        public void ChannelAftertouch_AbsentAfterCc121_AndAfterAllNotesOff()
        {
            var afterControllers = Encode(13, 10, (10, ChannelPressure(0, 40)), (11, Cc(0, 121, 0)));
            Assert.Equal(0, ChannelToc(afterControllers) & RtpMidiNoteJournal.TocT);

            var afterNotesOff = Encode(13, 10, (10, ChannelPressure(0, 40)), (11, Cc(0, 123, 0)));
            Assert.Equal(0, ChannelToc(afterNotesOff) & RtpMidiNoteJournal.TocT);
        }

        [Fact]
        public void ChannelAftertouch_RemainsAfterIndividualNoteOff()
        {
            var encoded = Encode(13, 10, (10, ChannelPressure(0, 15)), (11, NoteOff(0, 60, 64)));
            Assert.Equal(0x80 | 15, ChapterT(encoded));
        }

        [Fact]
        public void PolyAftertouch_IsOldestFirst_AndDecodesBack()
        {
            var encoded = Encode(
                13,
                10,
                (10, Poly(0, 64, 20)),
                (11, Poly(0, 60, 10)));
            Assert.Equal(RtpMidiNoteJournal.TocA, ChannelToc(encoded));
            var chapter = ChapterA(encoded);
            Assert.Equal(0x80 | 1, chapter[0]);
            Assert.Equal(0x80 | 64, chapter[1]);
            Assert.Equal(20, chapter[2]);
            Assert.Equal(0x80 | 60, chapter[3]);
            Assert.Equal(10, chapter[4]);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            Assert.Equal(64, commands[0].Data1);
            Assert.Equal(20, commands[0].Data2);
            Assert.Equal(60, commands[1].Data1);
            Assert.Equal(10, commands[1].Data2);
        }

        [Fact]
        public void PolyAftertouch_XSetAfterAllSoundOff_ButAbsentAfterCc121()
        {
            var afterSoundOff = Encode(13, 10, (10, Poly(0, 60, 30)), (11, Cc(0, 120, 0)));
            Assert.Equal(RtpMidiNoteJournal.TocA, ChannelToc(afterSoundOff) & RtpMidiNoteJournal.TocA);
            Assert.Equal(0x80 | 30, ChapterA(afterSoundOff)[2]);

            var afterControllers = Encode(13, 10, (10, Poly(0, 60, 30)), (11, Cc(0, 121, 0)));
            Assert.Equal(0, ChannelToc(afterControllers) & RtpMidiNoteJournal.TocA);
        }

        [Fact]
        public void PreviousPacketProgram_ClearsS()
        {
            var encoded = Encode(11, 10, (10, Program(0, 3)));
            Assert.Equal(0, encoded[0] & 0x80);
            Assert.Equal(0, ChapterP(encoded)[0] & 0x80);
        }

        [Fact]
        public void VoiceChapters_FollowTocOrder()
        {
            var encoded = Encode(
                16,
                10,
                (10, Program(1, 7)),
                (11, Cc(1, 7, 50)),
                (12, Pitch(1, 1, 2)),
                (13, ChannelPressure(1, 9)),
                (14, Poly(1, 40, 11)));
            Assert.Equal(1, (encoded[3] >> 3) & 0x0f);
            Assert.Equal(
                RtpMidiNoteJournal.TocP | RtpMidiNoteJournal.TocC | RtpMidiNoteJournal.TocW |
                RtpMidiNoteJournal.TocT | RtpMidiNoteJournal.TocA,
                ChannelToc(encoded));

            var body = 6;
            Assert.Equal(7, encoded[body] & 0x7f);
            var c = body + 3;
            Assert.Equal(7, encoded[c + 1] & 0x7f);
            var w = c + 1 + (((encoded[c] & 0x7f) + 1) * 2);
            Assert.Equal(1, encoded[w] & 0x7f);
            Assert.Equal(2, encoded[w + 1] & 0x7f);
            Assert.Equal(9, encoded[w + 2] & 0x7f);
            Assert.Equal(40, encoded[w + 4] & 0x7f);
        }

        [Fact]
        public void ResetState_DropsProgramPitchAndAftertouch()
        {
            var encoded = Encode(
                15,
                10,
                (10, Program(0, 1)),
                (11, Pitch(0, 1, 1)),
                (12, ChannelPressure(0, 2)),
                (13, Poly(0, 1, 2)),
                (14, new byte[] { 0xff }));
            Assert.Equal(0, encoded[0] & RtpMidiJournalSection.FlagA);
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

        private static byte[] Program(int channel, int program) =>
            new[] { (byte)(0xc0 | (channel & 0x0f)), (byte)program };

        private static byte[] Cc(int channel, int number, int value) =>
            new[] { (byte)(0xb0 | (channel & 0x0f)), (byte)number, (byte)value };

        private static byte[] Pitch(int channel, int first, int second) =>
            new[] { (byte)(0xe0 | (channel & 0x0f)), (byte)first, (byte)second };

        private static byte[] ChannelPressure(int channel, int pressure) =>
            new[] { (byte)(0xd0 | (channel & 0x0f)), (byte)pressure };

        private static byte[] Poly(int channel, int note, int pressure) =>
            new[] { (byte)(0xa0 | (channel & 0x0f)), (byte)note, (byte)pressure };

        private static byte[] NoteOff(int channel, int note, int velocity) =>
            new[] { (byte)(0x80 | (channel & 0x0f)), (byte)note, (byte)velocity };

        private static byte ChannelToc(byte[] journal) => journal[5];

        private static int Body(byte[] journal) => 6;

        private static byte[] ChapterP(byte[] journal)
        {
            var start = Body(journal);
            Assert.Equal(RtpMidiNoteJournal.TocP, ChannelToc(journal) & RtpMidiNoteJournal.TocP);
            return new[] { journal[start], journal[start + 1], journal[start + 2] };
        }

        private static byte[] ChapterC(byte[] journal)
        {
            var start = Body(journal);
            if ((ChannelToc(journal) & RtpMidiNoteJournal.TocP) == RtpMidiNoteJournal.TocP)
            {
                start += 3;
            }

            var logs = (journal[start] & 0x7f) + 1;
            var chapter = new byte[1 + (logs * 2)];
            System.Buffer.BlockCopy(journal, start, chapter, 0, chapter.Length);
            return chapter;
        }

        private static byte[] ChapterW(byte[] journal)
        {
            var start = SkipTo(journal, RtpMidiNoteJournal.TocW);
            return new[] { journal[start], journal[start + 1] };
        }

        private static byte ChapterT(byte[] journal) => journal[SkipTo(journal, RtpMidiNoteJournal.TocT)];

        private static byte[] ChapterA(byte[] journal)
        {
            var start = SkipTo(journal, RtpMidiNoteJournal.TocA);
            var logs = (journal[start] & 0x7f) + 1;
            var chapter = new byte[1 + (logs * 2)];
            System.Buffer.BlockCopy(journal, start, chapter, 0, chapter.Length);
            return chapter;
        }

        private static int SkipTo(byte[] journal, byte chapterBit)
        {
            var offset = Body(journal);
            var toc = ChannelToc(journal);
            if ((toc & RtpMidiNoteJournal.TocP) == RtpMidiNoteJournal.TocP && chapterBit != RtpMidiNoteJournal.TocP)
            {
                offset += 3;
            }

            if ((toc & RtpMidiNoteJournal.TocC) == RtpMidiNoteJournal.TocC &&
                chapterBit != RtpMidiNoteJournal.TocP &&
                chapterBit != RtpMidiNoteJournal.TocC)
            {
                offset += 1 + (((journal[offset] & 0x7f) + 1) * 2);
            }

            if ((toc & RtpMidiNoteJournal.TocM) == RtpMidiNoteJournal.TocM &&
                chapterBit != RtpMidiNoteJournal.TocP &&
                chapterBit != RtpMidiNoteJournal.TocC &&
                chapterBit != RtpMidiNoteJournal.TocM)
            {
                offset += RtpMidiJournalSection.ReadLength10(journal[offset], journal[offset + 1]);
            }

            if ((toc & RtpMidiNoteJournal.TocW) == RtpMidiNoteJournal.TocW &&
                chapterBit != RtpMidiNoteJournal.TocW &&
                chapterBit != RtpMidiNoteJournal.TocP &&
                chapterBit != RtpMidiNoteJournal.TocC &&
                chapterBit != RtpMidiNoteJournal.TocM)
            {
                offset += 2;
            }

            if ((toc & RtpMidiNoteJournal.TocN) == RtpMidiNoteJournal.TocN &&
                chapterBit != RtpMidiNoteJournal.TocN &&
                (chapterBit == RtpMidiNoteJournal.TocE ||
                 chapterBit == RtpMidiNoteJournal.TocT ||
                 chapterBit == RtpMidiNoteJournal.TocA))
            {
                offset += ChapterNLength(journal, offset);
            }

            if ((toc & RtpMidiNoteJournal.TocE) == RtpMidiNoteJournal.TocE &&
                (chapterBit == RtpMidiNoteJournal.TocT || chapterBit == RtpMidiNoteJournal.TocA))
            {
                offset += 1 + (((journal[offset] & 0x7f) + 1) * 2);
            }

            if ((toc & RtpMidiNoteJournal.TocT) == RtpMidiNoteJournal.TocT && chapterBit == RtpMidiNoteJournal.TocA)
            {
                offset += 1;
            }

            return offset;
        }

        private static int ChapterNLength(byte[] journal, int offset)
        {
            var logs = journal[offset] & 0x7f;
            var low = (journal[offset + 1] >> 4) & 0x0f;
            var high = journal[offset + 1] & 0x0f;
            var offOctets = low <= high ? high - low + 1 : 0;
            if (logs == 127 && low == 15 && high == 1)
            {
                offOctets = 0;
            }

            return 2 + (logs * 2) + offOctets;
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

        private static int ControllerValue(byte[] journal, int number)
        {
            var chapter = ChapterC(journal);
            var logs = (chapter[0] & 0x7f) + 1;
            for (var i = 0; i < logs; i++)
            {
                if ((chapter[1 + (i * 2)] & 0x7f) == number)
                {
                    return chapter[2 + (i * 2)] & 0x7f;
                }
            }

            return -1;
        }
    }
}
