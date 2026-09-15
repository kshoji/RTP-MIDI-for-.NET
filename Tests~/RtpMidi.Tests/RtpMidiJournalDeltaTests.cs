using System.Collections.Generic;
using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiJournalDeltaTests
    {
        [Fact]
        public void SustainOnOffOn_LostOff_ReplaysOffThenOn()
        {
            var encoded = Encode(
                13,
                (10, Cc(0, 64, 127)),
                (11, Cc(0, 64, 0)),
                (12, Cc(0, 64, 127)));
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));

            var state = new RtpMidiReceiveState();
            state.ObserveMidi(MidiType.ControlChange, Cc(0, 64, 127));

            var delta = state.Diff(recovered);

            Assert.Equal(2, delta.Count);
            Assert.All(delta, c => Assert.Equal(MidiType.ControlChange, c.Type));
            Assert.Equal(64, delta[0].Data1);
            Assert.Equal(0, delta[0].Data2);
            Assert.Equal(64, delta[1].Data1);
            Assert.Equal(127, delta[1].Data2);
        }

        [Fact]
        public void SustainOnOffOn_LostOffThenCurrentOn_JournalOnlyEmitsOff()
        {
            var encoded = Encode(12, (10, Cc(0, 64, 127)), (11, Cc(0, 64, 0)));
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));

            var state = new RtpMidiReceiveState();
            state.ObserveMidi(MidiType.ControlChange, Cc(0, 64, 127));

            var delta = state.Diff(recovered);

            Assert.Equal(new[] { 0 }, Values(delta, 64));
            Assert.Empty(state.Diff(recovered));
        }

        [Fact]
        public void OverlappingNoteOn_UsesChapterECount()
        {
            var encoded = Encode(
                12,
                (10, NoteOn(0, 60, 40)),
                (11, NoteOn(0, 60, 110)));
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));
            Assert.Contains(recovered, c => c.Type == MidiType.NoteOn && c.NoteRefCount == 2);

            var state = new RtpMidiReceiveState();
            state.ObserveMidi(MidiType.NoteOn, NoteOn(0, 60, 40));

            var delta = state.Diff(recovered);

            Assert.Single(delta);
            Assert.Equal(MidiType.NoteOn, delta[0].Type);
            Assert.Equal(60, delta[0].Data1);
            Assert.Equal(110, delta[0].Data2);
        }

        [Fact]
        public void OverlappingNoteOn_EmptyReceiverEmitsStackedNoteOns()
        {
            var encoded = Encode(
                12,
                (10, NoteOn(0, 60, 40)),
                (11, NoteOn(0, 60, 110)));
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));

            var delta = new RtpMidiReceiveState().Diff(recovered);

            Assert.Equal(2, delta.Count);
            Assert.All(delta, c => Assert.Equal(MidiType.NoteOn, c.Type));
            Assert.Equal(110, delta[0].Data2);
            Assert.Equal(110, delta[1].Data2);
        }

        [Fact]
        public void MatchingVolume_IsNotReplayed()
        {
            var encoded = Encode(12, (10, Cc(0, 7, 80)));
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));

            var state = new RtpMidiReceiveState();
            state.ObserveMidi(MidiType.ControlChange, Cc(0, 7, 80));

            Assert.Empty(state.Diff(recovered));
        }

        [Fact]
        public void ChangedVolume_IsSetOnce()
        {
            var encoded = Encode(12, (10, Cc(0, 7, 100)));
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));

            var state = new RtpMidiReceiveState();
            state.ObserveMidi(MidiType.ControlChange, Cc(0, 7, 80));

            var delta = state.Diff(recovered);
            Assert.Single(delta);
            Assert.Equal(7, delta[0].Data1);
            Assert.Equal(100, delta[0].Data2);
        }

        [Fact]
        public void AllNotesOff_IsNotDoubledWhenCountMatches()
        {
            var encoded = Encode(12, (10, Cc(0, 123, 0)));
            Assert.True(RtpMidiNoteJournal.TryDecode(encoded, encoded.Length, out var recovered));

            var state = new RtpMidiReceiveState();
            state.ObserveMidi(MidiType.ControlChange, Cc(0, 123, 0));

            Assert.Empty(state.Diff(recovered));
        }

        [Fact]
        public void InOrderPacket_DoesNotApplyJournal()
        {
            var kind = RtpMidiReceivePolicy.Classify(false, 10, 11);
            Assert.Equal(RtpPacketReceiveKind.InOrder, kind);
            Assert.False(RtpMidiReceivePolicy.ShouldApplyJournal(kind));
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

        private static byte[] NoteOn(int channel, int note, int velocity) =>
            new[] { (byte)(0x90 | (channel & 0x0f)), (byte)note, (byte)velocity };

        private static List<int> Values(List<RecoveredMidi> commands, int controller)
        {
            var values = new List<int>();
            foreach (var command in commands)
            {
                if (command.Type == MidiType.ControlChange && command.Data1 == controller)
                {
                    values.Add(command.Data2);
                }
            }

            return values;
        }
    }
}
