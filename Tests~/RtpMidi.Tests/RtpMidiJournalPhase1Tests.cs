using System.Collections.Generic;
using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiJournalPhase1Tests
    {
        [Theory]
        [InlineData(true, (ushort)0, (ushort)10, RtpPacketReceiveKind.First)]
        [InlineData(false, (ushort)10, (ushort)11, RtpPacketReceiveKind.InOrder)]
        [InlineData(false, (ushort)10, (ushort)12, RtpPacketReceiveKind.Loss)]
        [InlineData(false, (ushort)10, (ushort)10, RtpPacketReceiveKind.Reordered)]
        [InlineData(false, (ushort)10, (ushort)9, RtpPacketReceiveKind.Reordered)]
        [InlineData(false, (ushort)0xffff, (ushort)0, RtpPacketReceiveKind.InOrder)]
        [InlineData(false, (ushort)0xfffe, (ushort)0, RtpPacketReceiveKind.Loss)]
        [InlineData(false, (ushort)1, (ushort)0xffff, RtpPacketReceiveKind.Reordered)]
        public void Classify_MatchesRfcReceiveCases(bool first, ushort highest, ushort incoming, RtpPacketReceiveKind expected)
        {
            Assert.Equal(expected, RtpMidiReceivePolicy.Classify(first, highest, incoming));
        }

        [Theory]
        [InlineData(RtpPacketReceiveKind.First, true, true)]
        [InlineData(RtpPacketReceiveKind.Loss, true, true)]
        [InlineData(RtpPacketReceiveKind.InOrder, false, true)]
        [InlineData(RtpPacketReceiveKind.Reordered, false, false)]
        public void Policy_ApplyAndPlayFlags(RtpPacketReceiveKind kind, bool applyJournal, bool playMidi)
        {
            Assert.Equal(applyJournal, RtpMidiReceivePolicy.ShouldApplyJournal(kind));
            Assert.Equal(playMidi, RtpMidiReceivePolicy.ShouldPlayMidi(kind));
        }

        [Fact]
        public void ReorderedPacket_DoesNotReplayMidi_PreventingDoubleNoteOn()
        {
            // After accepting seq 5, a delayed duplicate/late seq 5 must not be played again.
            var kind = RtpMidiReceivePolicy.Classify(false, 5, 5);
            Assert.Equal(RtpPacketReceiveKind.Reordered, kind);
            Assert.False(RtpMidiReceivePolicy.ShouldPlayMidi(kind));
            Assert.False(RtpMidiReceivePolicy.ShouldApplyJournal(kind));
        }

        [Fact]
        public void InOrderPacket_DoesNotApplyJournal()
        {
            var kind = RtpMidiReceivePolicy.Classify(false, 100, 101);
            Assert.Equal(RtpPacketReceiveKind.InOrder, kind);
            Assert.False(RtpMidiReceivePolicy.ShouldApplyJournal(kind));
            Assert.True(RtpMidiReceivePolicy.ShouldPlayMidi(kind));
        }

        [Fact]
        public void FirstPacket_AppliesJournal()
        {
            var kind = RtpMidiReceivePolicy.Classify(true, 0, 42);
            Assert.Equal(RtpPacketReceiveKind.First, kind);
            Assert.True(RtpMidiReceivePolicy.ShouldApplyJournal(kind));
        }

        [Theory]
        [InlineData((ushort)11, (ushort)10, true)]   // C == highest+1
        [InlineData((ushort)10, (ushort)10, true)]   // C == highest
        [InlineData((ushort)5, (ushort)10, true)]    // C older than highest
        [InlineData((ushort)12, (ushort)10, false)]  // C beyond highest+1
        [InlineData((ushort)0, (ushort)0xffff, true)] // wrap: highest+1 == 0
        [InlineData((ushort)1, (ushort)0xffff, false)]
        public void CheckpointCoversLoss_MatchesRfcRule(ushort checkpoint, ushort highestBefore, bool expected)
        {
            Assert.Equal(expected, RtpMidiReceivePolicy.CheckpointCoversLoss(checkpoint, highestBefore));
        }

        [Fact]
        public void ClearIndefiniteState_EmitsAllSoundOffResetAndNotesOffOnAllChannels()
        {
            var handler = new RecordingHandler();
            RtpMidiIndefiniteState.Clear(handler, "device-1");

            Assert.Equal(16 * 3, handler.ControlChanges.Count);
            for (var channel = 0; channel < 16; channel++)
            {
                Assert.Contains(handler.ControlChanges, c => c.channel == channel && c.controller == RtpMidiIndefiniteState.AllSoundOff);
                Assert.Contains(handler.ControlChanges, c => c.channel == channel && c.controller == RtpMidiIndefiniteState.ResetAllControllers);
                Assert.Contains(handler.ControlChanges, c => c.channel == channel && c.controller == RtpMidiIndefiniteState.AllNotesOff);
            }
        }

        [Fact]
        public void EmptyJournal_CheckpointReadableForCoverageCheck()
        {
            var journal = RtpMidiJournalSection.EncodeEmpty(0x0011);
            Assert.True(RtpMidiReceivePolicy.CheckpointCoversLoss(
                RtpMidiJournalSection.GetCheckpointPacketSeqnum(journal),
                0x0010));
            Assert.False(RtpMidiReceivePolicy.CheckpointCoversLoss(
                RtpMidiJournalSection.GetCheckpointPacketSeqnum(RtpMidiJournalSection.EncodeEmpty(0x0012)),
                0x0010));
        }

        private sealed class RecordingHandler : IRtpMidiEventHandler
        {
            public readonly List<(int channel, int controller, int value)> ControlChanges = new();

            public void OnMidiNoteOn(string deviceId, int channel, int note, int velocity) { }
            public void OnMidiNoteOff(string deviceId, int channel, int note, int velocity) { }
            public void OnMidiPolyphonicAftertouch(string deviceId, int channel, int note, int pressure) { }
            public void OnMidiControlChange(string deviceId, int channel, int function, int value) =>
                ControlChanges.Add((channel, function, value));
            public void OnMidiProgramChange(string deviceId, int channel, int program) { }
            public void OnMidiChannelAftertouch(string deviceId, int channel, int pressure) { }
            public void OnMidiPitchWheel(string deviceId, int channel, int amount) { }
            public void OnMidiSystemExclusive(string deviceId, byte[] systemExclusive) { }
            public void OnMidiTimeCodeQuarterFrame(string deviceId, int timing) { }
            public void OnMidiSongSelect(string deviceId, int song) { }
            public void OnMidiSongPositionPointer(string deviceId, int position) { }
            public void OnMidiTuneRequest(string deviceId) { }
            public void OnMidiTimingClock(string deviceId) { }
            public void OnMidiStart(string deviceId) { }
            public void OnMidiContinue(string deviceId) { }
            public void OnMidiStop(string deviceId) { }
            public void OnMidiActiveSensing(string deviceId) { }
            public void OnMidiReset(string deviceId) { }
        }
    }
}
