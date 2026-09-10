using System.Collections.Generic;
using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiJournalPhase3Tests
    {
        [Fact]
        public void NoteOnOnly_EmitsNoteLog_NotOffbits()
        {
            var encoded = EncodeCommitted(12, (10, NoteOn(0, 60, 100)));

            var chapter = ChapterN(encoded, 0);
            Assert.Equal(0x81, chapter[0]); // B=1, LEN=1
            Assert.Equal(0xF0, chapter[1]); // LOW=15 HIGH=0, empty OFFBITS
            Assert.Equal(4, chapter.Length);
            Assert.Equal(0x80 | (60 << 1) | 1, chapter[2]);
            Assert.Equal(100, chapter[3]);
            Assert.Equal(0xA0, encoded[0]); // S=1, A=1, TOTCHAN=0
        }

        [Fact]
        public void NoteOffOnly_OffbitsMsbIsLowNote()
        {
            var encoded = EncodeCommitted(12, (10, NoteOff(0, 8, 40)), (10, NoteOff(0, 9, 40)));
            var chapter = ChapterN(encoded, 0);

            Assert.Equal(0x80, chapter[0]); // B=1, LEN=0
            Assert.Equal((1 << 4) | 1, chapter[1]); // LOW=1 HIGH=1, notes 8 and 9
            Assert.Equal(3, chapter.Length);
            Assert.Equal(0xC0, chapter[2]); // MSB = note 8, next = note 9
        }

        [Fact]
        public void NoteOnThenNoteOff_NotInBothStructures()
        {
            var encoded = EncodeCommitted(
                12,
                (10, NoteOn(0, 60, 100)),
                (11, NoteOff(0, 60, 40)));
            var chapter = ChapterN(encoded, 0);

            Assert.Equal(0, chapter[0] & 0x7f);
            Assert.Contains(60, OffNotes(chapter));
            Assert.Empty(NoteLogNotes(chapter));
        }

        [Fact]
        public void VelocityZeroNoteOn_IsNoteOff()
        {
            var encoded = EncodeCommitted(12, (10, NoteOn(0, 60, 90)), (11, new byte[] { 0x90, 60, 0 }));
            var chapter = ChapterN(encoded, 0);
            Assert.Equal(0, chapter[0] & 0x7f);
            Assert.Contains(60, OffNotes(chapter));
            Assert.False(HasChapterE(encoded, 0));
        }

        [Fact]
        public void NoteOffVelocityOtherThan64_IsChapterE()
        {
            var encoded = EncodeCommitted(13, (10, NoteOn(0, 60, 90)), (11, NoteOff(0, 60, 20)));
            Assert.True(HasChapterE(encoded, 0));
            var extra = ChapterE(encoded, 0);
            Assert.Equal(0x80, extra[0]); // S=1, LEN=0 (one log)
            Assert.Equal(0x80 | (60 << 1) | 1, extra[1]); // V=1
            Assert.Equal(20, extra[2]);
        }

        [Fact]
        public void OverlappingNoteOn_EmitsReferenceCount()
        {
            var encoded = EncodeCommitted(
                12,
                (10, NoteOn(0, 60, 40)),
                (11, NoteOn(0, 60, 110)));
            var chapter = ChapterN(encoded, 0);
            Assert.Equal(1, chapter[0] & 0x7f);
            Assert.Equal(110, chapter[3]); // most recent velocity

            var extra = ChapterE(encoded, 0);
            Assert.Equal((60 << 1), extra[1]); // S=0 because the NoteOn is in packet I-1, V=0
            Assert.Equal(2, extra[2]);
        }

        [Fact]
        public void AllNotesOff_DropsNActive()
        {
            var encoded = EncodeCommitted(
                13,
                (10, NoteOn(0, 60, 100)),
                (11, new byte[] { 0xb0, 123, 0 }),
                (12, NoteOn(0, 62, 80)));
            var chapter = ChapterN(encoded, 0);
            Assert.Equal(1, chapter[0] & 0x7f);
            Assert.Equal(62, (chapter[2] >> 1) & 0x7f);
            Assert.DoesNotContain(60, NoteLogNotes(chapter));
            Assert.DoesNotContain(60, OffNotes(chapter));
        }

        [Fact]
        public void AllSoundOffAndReset_DropNActive()
        {
            var soundOff = EncodeCommitted(
                12,
                (10, NoteOn(0, 60, 100)),
                (11, new byte[] { 0xb0, 120, 0 }));
            Assert.Equal(0, encodedFlags(soundOff) & 0x20);

            var reset = EncodeCommitted(
                12,
                (10, NoteOn(1, 60, 100)),
                (11, new byte[] { 0xff }));
            Assert.Equal(0, encodedFlags(reset) & 0x20);
        }

        [Fact]
        public void PreviousPacketNoteOff_ClearsBAndPropagatesS()
        {
            var encoded = EncodeCommitted(12, (10, NoteOn(0, 60, 100)), (11, NoteOff(0, 60, 40)));
            var chapter = ChapterN(encoded, 0);
            Assert.Equal(0, chapter[0] & 0x80); // B=0
            var channel = ChannelHeader(encoded, 0);
            Assert.Equal(0, channel[0] & 0x80);
            Assert.Equal(0, encoded[0] & 0x80); // top-level S=0
        }

        [Fact]
        public void PreviousPacketNoteOn_ClearsNoteLogS()
        {
            var encoded = EncodeCommitted(11, (10, NoteOn(0, 60, 100)));
            var chapter = ChapterN(encoded, 0);
            Assert.Equal(0, chapter[2] & 0x80);
            Assert.Equal(0, encoded[0] & 0x80);
        }

        [Fact]
        public void OneHundredTwentyEightNoteOns_UseSpecialLen()
        {
            var items = new (ushort, byte[])[128];
            for (var note = 0; note < 128; note++)
            {
                items[note] = ((ushort)10, NoteOn(0, note, 64));
            }

            var encoded = EncodeCommitted(11, items);
            var chapter = ChapterN(encoded, 0);
            Assert.Equal(127, chapter[0] & 0x7f);
            Assert.Equal(0xF0, chapter[1]); // LOW=15 HIGH=0, 128 logs, no OFFBITS
            Assert.Equal(2 + (128 * 2), chapter.Length);
        }

        [Fact]
        public void ChannelsAreAscending_LengthIncludesHeader()
        {
            var encoded = EncodeCommitted(
                11,
                (10, NoteOn(2, 10, 70)),
                (10, NoteOn(0, 12, 80)));

            Assert.Equal(1, encoded[0] & 0x0f); // TOTCHAN+1 = 2
            var first = ChannelHeader(encoded, 0);
            var secondOffset = 3 + RtpMidiJournalSection.ReadLength10(first[0], first[1]);
            var second = new[] { encoded[secondOffset], encoded[secondOffset + 1], encoded[secondOffset + 2] };
            Assert.Equal(0, (first[0] >> 3) & 0x0f);
            Assert.Equal(2, (second[0] >> 3) & 0x0f);
            Assert.Equal(3 + 2 + 2, RtpMidiJournalSection.ReadLength10(first[0], first[1]));
            Assert.Equal(0, first[0] & 0x04); // H=0
        }

        [Fact]
        public void CurrentPacketMidi_IsNotInJournal()
        {
            var journal = new RtpMidiJournal();
            journal.Record(NoteOn(0, 60, 100));
            journal.CommitPending(10);
            journal.Record(NoteOn(0, 62, 100));

            var encoded = journal.Encode(11, 10);
            var chapter = ChapterN(encoded, 0);
            Assert.Contains(60, NoteLogNotes(chapter));
            Assert.DoesNotContain(62, NoteLogNotes(chapter));
            Assert.Equal(2, journal.HistoryCount);
            Assert.Equal(1, journal.CommittedCount);
        }

        [Fact]
        public void LostNoteOff_DecodesToNoteOffWithChapterEVelocity()
        {
            var encoded = EncodeCommitted(12, (10, NoteOn(0, 60, 100)), (11, NoteOff(0, 60, 20)));
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));

            Assert.Contains(commands, c => c.Type == MidiType.NoteOff && c.Channel == 0 && c.Data1 == 60 && c.Data2 == 20);
            Assert.DoesNotContain(commands, c => c.Type == MidiType.NoteOn);
            Assert.Contains(commands, c => c.Type == MidiType.ControlChange && c.Data1 == 64 && c.Data2 == 0);
        }

        [Fact]
        public void Encode_DoesNotMutateHistory()
        {
            var journal = new RtpMidiJournal();
            journal.Record(NoteOn(0, 60, 100));
            journal.CommitPending(10);
            var first = journal.Encode(11, 10);
            var second = journal.Encode(11, 10);
            Assert.Equal(first, second);
            Assert.Equal(1, journal.HistoryCount);
        }

        [Fact]
        public void WrapAroundCheckpoint_StillEncodesNoteOff()
        {
            var journal = new RtpMidiJournal();
            journal.Record(NoteOn(0, 60, 100));
            journal.CommitPending(0xfffe);
            journal.Record(NoteOff(0, 60, 0));
            journal.CommitPending(0xffff);

            var encoded = journal.Encode(1, 0xfffe);
            Assert.Contains(60, OffNotes(ChapterN(encoded, 0)));
        }

        private static byte[] EncodeCommitted(ushort packetI, params (ushort seq, byte[] midi)[] items)
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

            return journal.Encode(packetI, items.Length == 0 ? packetI : items[0].seq);
        }

        private static byte encodedFlags(byte[] encoded) => encoded[0];

        private static byte[] NoteOn(int channel, int note, int velocity) =>
            new[] { (byte)(0x90 | (channel & 0x0f)), (byte)note, (byte)velocity };

        private static byte[] NoteOff(int channel, int note, int velocity) =>
            new[] { (byte)(0x80 | (channel & 0x0f)), (byte)note, (byte)velocity };

        private static byte[] ChannelHeader(byte[] journal, int index)
        {
            var offset = 3;
            for (var i = 0; i < index; i++)
            {
                offset += RtpMidiJournalSection.ReadLength10(journal[offset], journal[offset + 1]);
            }

            return new[] { journal[offset], journal[offset + 1], journal[offset + 2] };
        }

        private static byte[] ChapterN(byte[] journal, int channelIndex)
        {
            var offset = ChannelBody(journal, channelIndex);
            var toc = journal[ChannelHeaderOffset(journal, channelIndex) + 2];
            Assert.Equal(RtpMidiNoteJournal.TocN, toc & RtpMidiNoteJournal.TocN);
            var length = ChapterNLength(journal, offset);
            var chapter = new byte[length];
            System.Buffer.BlockCopy(journal, offset, chapter, 0, length);
            return chapter;
        }

        private static byte[] ChapterE(byte[] journal, int channelIndex)
        {
            var body = ChannelBody(journal, channelIndex);
            var nLength = ChapterNLength(journal, body);
            var start = body + nLength;
            var logs = (journal[start] & 0x7f) + 1;
            var chapter = new byte[1 + (logs * 2)];
            System.Buffer.BlockCopy(journal, start, chapter, 0, chapter.Length);
            return chapter;
        }

        private static bool HasChapterE(byte[] journal, int channelIndex)
        {
            var header = ChannelHeader(journal, channelIndex);
            return (header[2] & RtpMidiNoteJournal.TocE) == RtpMidiNoteJournal.TocE;
        }

        private static int ChannelHeaderOffset(byte[] journal, int index)
        {
            var offset = 3;
            for (var i = 0; i < index; i++)
            {
                offset += RtpMidiJournalSection.ReadLength10(journal[offset], journal[offset + 1]);
            }

            return offset;
        }

        private static int ChannelBody(byte[] journal, int index) => ChannelHeaderOffset(journal, index) + 3;

        private static int ChapterNLength(byte[] journal, int offset)
        {
            var len = journal[offset] & 0x7f;
            var low = journal[offset + 1] >> 4;
            var high = journal[offset + 1] & 0x0f;
            var logs = len;
            var offs = 0;
            if (low == 15 && high == 0)
            {
                if (len == 127)
                {
                    logs = 128;
                }
            }
            else if (low == 15 && high == 1)
            {
                offs = 0;
            }
            else if (low <= high)
            {
                offs = high - low + 1;
            }

            return 2 + (logs * 2) + offs;
        }

        private static List<int> NoteLogNotes(byte[] chapter)
        {
            var notes = new List<int>();
            var len = chapter[0] & 0x7f;
            var low = chapter[1] >> 4;
            var high = chapter[1] & 0x0f;
            var logs = len == 127 && low == 15 && high == 0 ? 128 : len;
            for (var i = 0; i < logs; i++)
            {
                notes.Add((chapter[2 + (i * 2)] >> 1) & 0x7f);
            }

            return notes;
        }

        private static List<int> OffNotes(byte[] chapter)
        {
            var notes = new List<int>();
            var len = chapter[0] & 0x7f;
            var low = chapter[1] >> 4;
            var high = chapter[1] & 0x0f;
            var logs = len;
            var offOctets = 0;
            if (low == 15 && high == 0)
            {
                if (len == 127)
                {
                    logs = 128;
                }
            }
            else if (!(low == 15 && high == 1) && low <= high)
            {
                offOctets = high - low + 1;
            }

            var start = 2 + (logs * 2);
            for (var octet = 0; octet < offOctets; octet++)
            {
                var bits = chapter[start + octet];
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((bits & (0x80 >> bit)) != 0)
                    {
                        notes.Add((low + octet) * 8 + bit);
                    }
                }
            }

            return notes;
        }
    }
}
