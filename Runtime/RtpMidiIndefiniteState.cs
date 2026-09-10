namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Local renderer cleanup for indefinite MIDI artifacts (RFC 6295 recovery journal mandate).
    /// </summary>
    public static class RtpMidiIndefiniteState
    {
        public const int AllSoundOff = 120;
        public const int ResetAllControllers = 121;
        public const int AllNotesOff = 123;

        /// <summary>
        /// Emits All Sound Off, Reset All Controllers, and All Notes Off on every channel.
        /// Used on session exit and when a recovery journal cannot cover a deep loss.
        /// </summary>
        public static void Clear(IRtpMidiEventHandler handler, string deviceId)
        {
            if (handler == null || deviceId == null)
            {
                return;
            }

            for (var channel = 0; channel < 16; channel++)
            {
                handler.OnMidiControlChange(deviceId, channel, AllSoundOff, 0);
                handler.OnMidiControlChange(deviceId, channel, ResetAllControllers, 0);
                handler.OnMidiControlChange(deviceId, channel, AllNotesOff, 0);
            }
        }
    }
}
