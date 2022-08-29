using System.Net;
using System.Threading;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Listener for RTP MIDI device connection
    /// </summary>
    public interface IRtpMidiDeviceConnectionListener
    {
        /// <summary>
        /// Called on the RTP MIDI connection started
        /// </summary>
        /// <param name="deviceId">the device ID</param>
        void OnRtpMidiDeviceAttached(string deviceId);

        /// <summary>
        /// Called on the RTP MIDI connection finished
        /// </summary>
        /// <param name="deviceId">the device ID</param>
        void OnRtpMidiDeviceDetached(string deviceId);
    }

    /// <summary>
    /// Serves RTP MIDI connections
    /// </summary>
    public class RtpMidiServer
    {
        private RtpMidiThread rtpMidiThread;
        private readonly RtpMidiSession session;

        /// <summary>
        /// Obtains the name of session, and ssid from deviceId
        /// </summary>
        /// <param name="deviceId">the device id, notified by <see cref="IRtpMidiDeviceConnectionListener"/></param>
        /// <returns>the name of session, and ssid. null if not found.</returns>
        public string GetDeviceName(string deviceId)
        {
            var participant = session.GetParticipantFromDeviceId(deviceId);
            return participant == null ? null : $"{participant.sessionName},${participant.ssrc}";
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="sessionName">the session name</param>
        /// <param name="listenPort">UDP control port(0-65534)</param>
        /// <param name="deviceConnectionListener">device connection listener</param>
        /// <param name="rtpMidiEventHandler">MIDI event handler</param>
        public RtpMidiServer(string sessionName, int listenPort, IRtpMidiDeviceConnectionListener deviceConnectionListener, IRtpMidiEventHandler rtpMidiEventHandler)
        {
            session = new RtpMidiSession(sessionName, listenPort, deviceConnectionListener);
            session.SetMidiEventListener(rtpMidiEventHandler);
        }

        /// <summary>
        /// Connect to the another RTP MIDI Listener endpoint
        /// </summary>
        /// <param name="endPoint"></param>
        public void ConnectToListener(IPEndPoint endPoint)
        {
            session.SendInvite(endPoint);
        }

        /// <summary>
        /// Starts the service thread
        /// </summary>
        public void Start()
        {
            if (rtpMidiThread == null)
            {
                rtpMidiThread = new RtpMidiThread(session);
            }
        }

        /// <summary>
        /// Check if the thread running
        /// </summary>
        /// <returns>the thread is running</returns>
        public bool IsStarted()
        {
            if (rtpMidiThread == null)
            {
                return false;
            }

            return rtpMidiThread.IsRunning;
        }

        /// <summary>
        /// Stops the service thread
        /// </summary>
        public void Stop()
        {
            rtpMidiThread?.Stop();
            rtpMidiThread = null;
        }

        /// <summary>
        /// Send a Note On message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="channel">the channel</param>
        /// <param name="note">the note number</param>
        /// <param name="velocity">the velocity</param>
        public void SendMidiNoteOn(string deviceId, int channel, int note, int velocity)
        {
            SendMidiRaw(deviceId, new[] {(byte)((channel & 0xf) | 0x90), (byte)(note & 0x7f), (byte)(velocity & 0x7f)});
        }

        /// <summary>
        /// Send a Note Off message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="channel">the channel</param>
        /// <param name="note">the note number</param>
        /// <param name="velocity">the velocity</param>
        public void SendMidiNoteOff(string deviceId, int channel, int note, int velocity)
        {
            SendMidiRaw(deviceId, new[] {(byte)((channel & 0xf) | 0x80), (byte)(note & 0x7f), (byte)(velocity & 0x7f)});
        }

        /// <summary>
        /// Send a Polyphonic Aftertouch message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="channel">the channel</param>
        /// <param name="note">the note number</param>
        /// <param name="pressure">the pressure</param>
        public void SendMidiPolyphonicAftertouch(string deviceId, int channel, int note, int pressure)
        {
            SendMidiRaw(deviceId, new[] {(byte)((channel & 0xf) | 0xa0), (byte)(note & 0x7f), (byte)(pressure & 0x7f)});
        }

        /// <summary>
        /// Send a Control Change message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="channel">the channel</param>
        /// <param name="function">the function</param>
        /// <param name="value">the value</param>
        public void SendMidiControlChange(string deviceId, int channel, int function, int value)
        {
            SendMidiRaw(deviceId, new[] {(byte)((channel & 0xf) | 0xb0), (byte)(function & 0x7f), (byte)(value & 0x7f)});
        }

        /// <summary>
        /// Send a Program Change message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="channel">the channel</param>
        /// <param name="program">the program</param>
        public void SendMidiProgramChange(string deviceId, int channel, int program)
        {
            SendMidiRaw(deviceId, new[] {(byte)((channel & 0xf) | 0xc0), (byte)(program & 0x7f)});
        }

        /// <summary>
        /// Send a Channel Aftertouch message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="channel">the channel</param>
        /// <param name="pressure">the pressure</param>
        public void SendMidiChannelAftertouch(string deviceId, int channel, int pressure)
        {
            SendMidiRaw(deviceId, new[] {(byte)((channel & 0xf) | 0xd0), (byte)(pressure & 0x7f)});
        }

        /// <summary>
        /// Send a Pitch Wheel message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="channel">the channel</param>
        /// <param name="amount">the amount</param>
        public void SendMidiPitchWheel(string deviceId, int channel, int amount)
        {
            SendMidiRaw(deviceId, new[] {(byte)((channel & 0xf) | 0xe0), (byte)(amount & 0x7f), (byte)((amount >> 7) & 0x7f)});
        }

        /// <summary>
        /// Send a System Exclusive message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="systemExclusive">the system exclusive</param>
        public void SendMidiSystemExclusive(string deviceId, byte[] systemExclusive)
        {
            SendMidiRaw(deviceId, systemExclusive);
        }

        /// <summary>
        /// Send a Time Code Quarter Frame message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="timing">the timing</param>
        public void SendMidiTimeCodeQuarterFrame(string deviceId, int timing)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xf1, (byte)(timing & 0x7f)});
        }

        /// <summary>
        /// Send a Song Select message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="song">the song</param>
        public void SendMidiSongSelect(string deviceId, int song)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xf3, (byte)(song & 0x7f)});
        }

        /// <summary>
        /// Send a Song Position Pointer message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        /// <param name="position">the position</param>
        public void SendMidiSongPositionPointer(string deviceId, int position)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xf2, (byte)(position & 0x7f), (byte)((position >> 7) & 0x7f)});
        }

        /// <summary>
        /// Send a Tune Request message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        public void SendMidiTuneRequest(string deviceId)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xf6});
        }

        /// <summary>
        /// Send a Timing Clock message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        public void SendMidiTimingClock(string deviceId)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xf8});
        }

        /// <summary>
        /// Send a Start message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        public void SendMidiStart(string deviceId)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xfa});
        }

        /// <summary>
        /// Send a Continue message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        public void SendMidiContinue(string deviceId)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xfb});
        }

        /// <summary>
        /// Send a Stop message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        public void SendMidiStop(string deviceId)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xfc});
        }

        /// <summary>
        /// Send a Active Sensing message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        public void SendMidiActiveSensing(string deviceId)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xfe});
        }

        /// <summary>
        /// Send a Reset message
        /// </summary>
        /// <param name="deviceId">the device id</param>
        public void SendMidiReset(string deviceId)
        {
            SendMidiRaw(deviceId, new[] {(byte)0xff});
        }

        private void SendMidiRaw(string deviceId, byte[] data)
        {
            var participant = session.GetParticipantFromDeviceId(deviceId);
            if (participant == null)
            {
                return;
            }

            if (session.BeginTransmission(participant))
            {
                foreach (var datum in data)
                {
                    session.Write(participant, datum);
                }
                session.EndTransmission(participant);
            }
        }

        class RtpMidiThread
        {
            internal bool IsRunning;
            private readonly Thread thread;
            internal RtpMidiThread(RtpMidiSession session)
            {
                thread = new Thread(() =>
                {
                    IsRunning = true;

                    session.Begin();
                    while (IsRunning)
                    {
                        session.ManageSessionInvites();
                        session.ReadDataPackets();

                        foreach (var participant in session.participants)
                        {
                            var length = session.Available(participant);
                            for (var i = 0; i < length; i++)
                            {
                                session.Read(participant);
                            }
                        }

                        if (session.ReadControlPackets() > 0)
                        {
                            session.ParseControlPackets();
                        }

                        session.ManageReceiverFeedback();
                        session.ManageSynchronization();

                        // wait for next data
                        if (thread != null)
                        {
                            lock (thread)
                            {
                                Monitor.Wait(thread, 10);
                            }
                        }
                    }
                    session.End();
                });
                
                thread.Start();
            }

            public void Stop()
            {
                IsRunning = false;
                lock (thread)
                {
                    Monitor.PulseAll(thread);
                }
            }
        }
    }
}