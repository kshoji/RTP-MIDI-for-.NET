using System.Collections.Generic;
using jp.kshoji.rtpmidi;
using Xunit;

namespace RtpMidi.Tests
{
    public class RtpMidiJournalPhase11Tests
    {
        [Fact]
        public void LocalClear_StillEmitsAllSoundOffRacAndNotesOffOnEveryChannel()
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
        public void ShouldNotifyPeer_OnlyWhenThisSideEndsAConnectedSession()
        {
            Assert.True(RtpMidiIndefiniteState.ShouldNotifyPeer(false, true));
            Assert.False(RtpMidiIndefiniteState.ShouldNotifyPeer(true, true));
            Assert.False(RtpMidiIndefiniteState.ShouldNotifyPeer(false, false));
        }

        [Fact]
        public void PeerClearCommands_ArePackedUnderMaxBufferAndParseBack()
        {
            var commands = RtpMidiIndefiniteState.PeerClearCommands();
            Assert.Equal(48, commands.Count);

            var sections = RtpMidiCommandSection.Pack(commands, RtpMidiParticipant.MaxBufferSize);
            Assert.NotEmpty(sections);
            Assert.All(sections, section => Assert.True(section.Length <= RtpMidiParticipant.MaxBufferSize));
            Assert.All(sections, section =>
                Assert.True(RtpMidiPayloadBudget.Fits(section.Length, RtpMidiJournalSection.EncodeEmpty(1).Length)));

            var parsed = new List<byte[]>();
            foreach (var section in sections)
            {
                parsed.AddRange(RtpMidiCommandSection.Parse(section));
            }

            Assert.Equal(48, parsed.Count);
            for (var channel = 0; channel < 16; channel++)
            {
                var offset = channel * 3;
                Assert.Equal(new byte[] { (byte)(0xb0 | channel), RtpMidiIndefiniteState.AllSoundOff, 0 }, parsed[offset]);
                Assert.Equal(new byte[] { (byte)(0xb0 | channel), RtpMidiIndefiniteState.ResetAllControllers, 0 }, parsed[offset + 1]);
                Assert.Equal(new byte[] { (byte)(0xb0 | channel), RtpMidiIndefiniteState.AllNotesOff, 0 }, parsed[offset + 2]);
            }
        }

        [Fact]
        public void VoluntaryExit_SendsPeerClearBeforeByeThenLocalClear()
        {
            var steps = new List<string>();
            if (RtpMidiIndefiniteState.ShouldNotifyPeer(peerInitiatedEnd: false, connected: true))
            {
                var sections = RtpMidiCommandSection.Pack(
                    RtpMidiIndefiniteState.PeerClearCommands(),
                    RtpMidiParticipant.MaxBufferSize);
                Assert.NotEmpty(sections);
                steps.Add("peer-clear");
            }

            steps.Add("bye");
            var handler = new RecordingHandler();
            RtpMidiIndefiniteState.Clear(handler, "device-1");
            steps.Add("local-clear");

            Assert.Equal(new[] { "peer-clear", "bye", "local-clear" }, steps);
            Assert.Equal(48, handler.ControlChanges.Count);
        }

        [Fact]
        public void IncomingBye_SkipsPeerClearAndStillClearsLocalRenderer()
        {
            var steps = new List<string>();
            if (RtpMidiIndefiniteState.ShouldNotifyPeer(peerInitiatedEnd: true, connected: true))
            {
                steps.Add("peer-clear");
            }

            var handler = new RecordingHandler();
            RtpMidiIndefiniteState.Clear(handler, "device-1");
            steps.Add("local-clear");

            Assert.Equal(new[] { "local-clear" }, steps);
            Assert.Equal(48, handler.ControlChanges.Count);
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
