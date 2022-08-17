namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Event handler for RTP MIDI
    /// </summary>
    public interface IRtpMidiEventHandler
    {
        void OnMidiNoteOn(string deviceId, int channel, int note, int velocity);
        void OnMidiNoteOff(string deviceId, int channel, int note, int velocity);
        void OnMidiPolyphonicAftertouch(string deviceId, int channel, int note, int pressure);
        void OnMidiControlChange(string deviceId, int channel, int function, int value);
        void OnMidiProgramChange(string deviceId, int channel, int program);
        void OnMidiChannelAftertouch(string deviceId, int channel, int pressure);
        void OnMidiPitchWheel(string deviceId, int channel, int amount);
        void OnMidiSystemExclusive(string deviceId, byte[] systemExclusive);
        void OnMidiTimeCodeQuarterFrame(string deviceId, int timing);
        void OnMidiSongSelect(string deviceId, int song);
        void OnMidiSongPositionPointer(string deviceId, int position);
        void OnMidiTuneRequest(string deviceId);
        void OnMidiTimingClock(string deviceId);
        void OnMidiStart(string deviceId);
        void OnMidiContinue(string deviceId);
        void OnMidiStop(string deviceId);
        void OnMidiActiveSensing(string deviceId);
        void OnMidiReset(string deviceId);
    }
}