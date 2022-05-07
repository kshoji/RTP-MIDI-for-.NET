namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Event handler for RTP MIDI
    /// </summary>
    public interface IRtpMidiEventHandler
    {
        void OnMidiNoteOn(int channel, int note, int velocity);
        void OnMidiNoteOff(int channel, int note, int velocity);
        void OnMidiPolyphonicAftertouch(int channel, int note, int pressure);
        void OnMidiControlChange(int channel, int function, int value);
        void OnMidiProgramChange(int channel, int program);
        void OnMidiChannelAftertouch(int channel, int pressure);
        void OnMidiPitchWheel(int channel, int amount);
        void OnMidiSystemExclusive(byte[] systemExclusive);
        void OnMidiTimeCodeQuarterFrame(int timing);
        void OnMidiSongSelect(int song);
        void OnMidiSongPositionPointer(int position);
        void OnMidiTuneRequest();
        void OnMidiTimingClock();
        void OnMidiStart();
        void OnMidiContinue();
        void OnMidiStop();
        void OnMidiActiveSensing();
        void OnMidiReset();
    }
}