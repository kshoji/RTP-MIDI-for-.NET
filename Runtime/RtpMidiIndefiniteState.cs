using System.Collections.Generic;

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

        /// <summary>
        /// Control Changes sent to a still-connected peer before a locally initiated BY.
        /// </summary>
        public static List<byte[]> PeerClearCommands()
        {
            var commands = new List<byte[]>(48);
            for (var channel = 0; channel < 16; channel++)
            {
                commands.Add(new[] { (byte)(0xb0 | channel), (byte)AllSoundOff, (byte)0 });
                commands.Add(new[] { (byte)(0xb0 | channel), (byte)ResetAllControllers, (byte)0 });
                commands.Add(new[] { (byte)(0xb0 | channel), (byte)AllNotesOff, (byte)0 });
            }

            return commands;
        }

        /// <summary>
        /// True when this side should send peer-clear MIDI before BY.
        /// Incoming BY means the peer already left.
        /// </summary>
        public static bool ShouldNotifyPeer(bool peerInitiatedEnd, bool connected)
        {
            return connected && !peerInitiatedEnd;
        }
    }
}
