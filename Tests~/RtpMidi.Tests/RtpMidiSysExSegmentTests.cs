using System.Collections.Generic;
using System.Linq;
using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiSysExSegmentTests
    {
        [Fact]
        public void SysExFittingInOnePacket_IsFinishedChapterX()
        {
            var sysex = new byte[] { 0xf0, 0x41, 0x10, 0xf7 };
            var segments = RtpMidiCommandSection.Segment(sysex, RtpMidiParticipant.MaxBufferSize);
            Assert.Single(segments);
            Assert.Equal(sysex, segments[0]);

            var journal = new RtpMidiJournal();
            journal.Record(segments[0]);
            journal.CommitPending(10);
            var encoded = journal.Encode(12, 10);
            Assert.Equal(3, ChapterX(encoded)[0] & 0x03);

            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var commands));
            var recovered = Assert.Single(commands, c => c.Type == MidiType.SystemExclusive);
            Assert.Equal(sysex, recovered.Payload);
        }

        [Fact]
        public void WriteSplitSysEx_FirstJournalUnfinished_LastFinished()
        {
            var sysex = MakeSysEx(80);
            var segments = RtpMidiCommandSection.Segment(sysex, RtpMidiParticipant.MaxBufferSize);
            Assert.Equal(2, segments.Count);
            Assert.Equal(0xf0, segments[0][0]);
            Assert.Equal(0xf0, segments[0][segments[0].Length - 1]);
            Assert.Equal(0xf7, segments[segments.Count - 1][0]);
            Assert.Equal(0xf7, segments[segments.Count - 1][segments[segments.Count - 1].Length - 1]);

            var journal = new RtpMidiJournal();
            journal.Record(segments[0]);
            journal.CommitPending(10);
            var afterFirst = journal.Encode(11, 10);
            Assert.Equal(0, ChapterX(afterFirst)[0] & 0x03);

            for (var i = 1; i < segments.Count; i++)
            {
                journal.Record(segments[i]);
                journal.CommitPending((ushort)(10 + i));
            }

            var finished = journal.Encode((ushort)(10 + segments.Count), 10);
            Assert.Equal(3, ChapterX(finished)[0] & 0x03);

            Assert.True(RtpMidiNoteJournal.TryDecode(finished, finished.Length, out var commands));
            var recovered = Assert.Single(commands, c => c.Type == MidiType.SystemExclusive && c.SysExStatus != 0);
            Assert.Equal(sysex, recovered.Payload);
        }

        [Fact]
        public void LostPacketsAcrossSplit_RecoverFinishedSysExFromJournal()
        {
            var sysex = MakeSysEx(80);
            var segments = RtpMidiCommandSection.Segment(sysex, RtpMidiParticipant.MaxBufferSize);
            var journal = new RtpMidiJournal();
            for (var i = 0; i < segments.Count; i++)
            {
                journal.Record(segments[i]);
                journal.CommitPending((ushort)(10 + i));
            }

            var encoded = journal.Encode((ushort)(10 + segments.Count), 10);
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));

            var state = new RtpMidiReceiveState();
            var delta = state.Diff(recovered);
            var played = Assert.Single(delta, c => c.Type == MidiType.SystemExclusive);
            Assert.Equal(sysex, played.Payload);
        }

        [Fact]
        public void UnfinishedJournalThenLastSegment_CompletesSysEx()
        {
            var sysex = MakeSysEx(80);
            var segments = RtpMidiCommandSection.Segment(sysex, RtpMidiParticipant.MaxBufferSize);
            var journal = new RtpMidiJournal();
            journal.Record(segments[0]);
            journal.CommitPending(10);
            var unfinished = journal.Encode(11, 10);
            Assert.True(RtpMidiNoteJournal.TryDecode(unfinished, unfinished.Length, out var recovered));

            var state = new RtpMidiReceiveState();
            Assert.Empty(state.Diff(recovered).Where(c => c.Type == MidiType.SystemExclusive && c.SysExStatus != 0));

            Assert.True(state.PushSysEx(segments[segments.Count - 1], out var finished));
            Assert.Equal(Join(segments), finished);
        }

        [Fact]
        public void CancelledJournal_DropsOpenSysEx()
        {
            var state = new RtpMidiReceiveState();
            Assert.False(state.PushSysEx(new byte[] { 0xf0, 0x11, 0xf0 }, out _));
            Assert.False(state.PushSysEx(new byte[] { 0xf7, 0xf4 }, out var finished));
            Assert.Null(finished);

            var encoded = Encode(13, 10, (10, new byte[] { 0xf0, 0x11, 0xf0 }), (11, new byte[] { 0xf7, 0xf4 }));
            Assert.Equal(1, ChapterX(encoded)[0] & 0x03);
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));
            Assert.Empty(state.Diff(recovered).Where(c => c.Type == MidiType.SystemExclusive && c.SysExStatus != 1 && c.Payload != null && c.Payload.Length > 0));
        }

        [Fact]
        public void MidiSectionParse_FindsEachWriteSegment()
        {
            var sysex = MakeSysEx(80);
            var segments = RtpMidiCommandSection.Segment(sysex, RtpMidiParticipant.MaxBufferSize);
            foreach (var segment in segments)
            {
                var commands = RtpMidiCommandSection.Parse(segment);
                Assert.Single(commands);
                Assert.Equal(segment, commands[0]);
            }
        }

        private static byte[] MakeSysEx(int length)
        {
            var bytes = new byte[length];
            bytes[0] = 0xf0;
            bytes[1] = 0x7d;
            for (var i = 2; i < length - 1; i++)
            {
                bytes[i] = (byte)(i & 0x7f);
            }

            bytes[length - 1] = 0xf7;
            return bytes;
        }

        private static byte[] Join(IReadOnlyList<byte[]> segments)
        {
            var data = new List<byte>();
            foreach (var segment in segments)
            {
                Assert.True(RtpMidiCommandSection.TryClassifySysEx(segment, out _, out var inner));
                data.AddRange(inner);
            }

            return RtpMidiCommandSection.WrapFinished(data.ToArray());
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

        private static byte[] ChapterX(byte[] journal)
        {
            var start = SkipTo(journal, RtpMidiSystemJournal.TocX);
            var end = 3 + RtpMidiJournalSection.ReadLength10(journal[3], journal[4]);
            var chapter = new byte[end - start];
            System.Buffer.BlockCopy(journal, start, chapter, 0, chapter.Length);
            return chapter;
        }

        private static int SkipTo(byte[] journal, byte chapter)
        {
            var offset = 5;
            var toc = (byte)(journal[3] & 0x7c);
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
    }
}
