using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Random = System.Random;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Kind of RTP MIDI exception
    /// </summary>
    public enum RtpMidiExceptionKind
    {
        BufferFullException,
        ParseException,
        UnexpectedParseException,
        TooManyParticipantsException,
        ParticipantNotFoundException,
        ListenerTimeOutException,
        MaxAttemptsException,
        NoResponseFromConnectionRequestException,
        SendPacketsDropped,
        ReceivedPacketsDropped,
        RecoveryJournalOverflowException,
    }

    /// <summary>
    /// Listener for RTP MIDI exception events
    /// </summary>
    public interface IRtpMidiExceptionListener
    {
        /// <summary>
        /// Notifies an error happens
        /// </summary>
        /// <param name="exceptionKind"></param>
        void OnError(RtpMidiExceptionKind exceptionKind);
    }

    /// <summary>
    /// Stores RTP headers
    /// </summary>
    public struct Rtp
    {
        internal byte vpxcc;
        internal byte mpayload;
        internal ushort sequenceNr;
        internal int timestamp;
        internal int ssrc;
    }

    /// <summary>
    /// The kind of participant
    /// </summary>
    public enum ParticipantKind
    {
        Listener,
        Initiator,
    }

    /// <summary>
    /// The connection status for listener invitation
    /// </summary>
    public enum InviteStatus
    {
        Initiating,
        AwaitingControlInvitationAccepted,
        ControlInvitationAccepted,
        AwaitingDataInvitationAccepted,
        DataInvitationAccepted,
        Connected,
    }

    /// <summary>
    /// Represents RTP MIDI connection
    /// </summary>
    public class RtpMidiParticipant
    {
        private static readonly Random random = new Random();

        internal ParticipantKind kind;
        internal int ssrc;
        internal readonly IPEndPoint ControlEndPoint;
        internal readonly IPEndPoint DataEndPoint;

        internal long lastReceiverFeedbackSentTime;
        internal bool hasReceivedRtp;

        internal uint sendSequenceNrExtended = (uint)random.Next(1, short.MaxValue);
        internal uint receiveSequenceNrExtended;
        internal ushort sendSequenceNr => RtpSequenceNumber.Low16(sendSequenceNrExtended);
        internal ushort receiveSequenceNr => RtpSequenceNumber.Low16(receiveSequenceNrExtended);
        internal int lostPacketCount;

#if ENABLE_RTP_MIDI_JOURNAL
        internal RtpPacketReceiveKind lastReceiveKind = RtpPacketReceiveKind.First;
        internal ushort highestReceivedSequenceBeforePacket;
        internal bool playMidiCommands = true;
        internal bool applyRecoveryJournal;
        internal ushort remoteFeedbackSequenceNr;
        internal uint remoteFeedbackExtended;
        internal bool hasRemoteFeedback;
        internal long lastRtpMidiSendTime;
        internal bool hasSentRtpMidi;
        internal uint firstSentSequenceExtended;
#endif

        internal long lastSyncExchangeTime;

        internal string sessionName;

        internal byte connectionAttempts;
        internal int initiatorToken;
        internal long lastInviteSentTime;
        internal InviteStatus invitationStatus = InviteStatus.Initiating;
        internal byte synchronizationHeartBeats;
        internal byte synchronizationCount;
        public bool Synchronizing { get; set; }

        public bool FirstMessageReceived = true;
        public long OffsetEstimate { get; set; }
        public const int MaxBufferSize = 64;
        public const int MaxUdpPayloadSize = 1200;

        internal readonly LinkedList<byte> inMidiBuffer = new LinkedList<byte>();
        internal readonly LinkedList<byte> outMidiBuffer = new LinkedList<byte>();

        internal readonly LinkedList<byte> dataBuffer = new LinkedList<byte>();

        internal readonly RtpMidiParser rtpMidiParser;

#if ENABLE_RTP_MIDI_JOURNAL
        internal readonly RtpMidiJournal journal;
#endif

        internal RtpMidiParticipant(RtpMidiSession session, IPEndPoint endPoint)
        {
            rtpMidiParser = new RtpMidiParser(session);
            ControlEndPoint = endPoint;
            DataEndPoint = new IPEndPoint(endPoint.Address, endPoint.Port + 1);
#if ENABLE_RTP_MIDI_JOURNAL
            journal = new RtpMidiJournal();
#endif
        }
    }

    /// <summary>
    /// Represents RTP MIDI session
    /// </summary>
    public class RtpMidiSession
    {
        private int Port { get; }

        private int Ssrc { get; set; }

        private readonly RtpMidiClock rtpMidiClock = new RtpMidiClock();
        private readonly Random random = new Random();

        private UdpClient controlPort;
        private IPEndPoint controlReceivedEndPoint;
        private UdpClient dataPort;
        private IPEndPoint dataReceivedEndPoint;
        private const int MaxParticipants = 64;
        internal readonly HashSet<RtpMidiParticipant> participants = new HashSet<RtpMidiParticipant>();
        private readonly HashSet<RtpMidiParticipant> participantsToRemove = new HashSet<RtpMidiParticipant>();

        private readonly LinkedList<byte> controlBuffer = new LinkedList<byte>();

        private readonly AppleMidiParser appleMidiParser;

        private const int MaxSessionInvitesAttempts = 13;

        private const int ReceiversFeedbackThreshold = 1000;

#if ENABLE_RTP_MIDI_JOURNAL
        private const int TrailingLossIntervalMs = 50;
        /// <summary>
        /// Disconnect when RS stalls and unacked send sequences exceed this span.
        /// </summary>
        private const uint MaxUnackedJournalPackets = 512;
#endif

        // The initiator must initiate a new sync exchange at least once every 60 seconds
        // as in https://developer.apple.com/library/archive/documentation/Audio/Conceptual/MIDINetworkDriverProtocol/MIDI/MIDI.html
        private const int CkMaxTimeOut = 61000;
        
        private const byte MaxSynchronizationCk0Attempts = 5;

        private const long SynchronizationHeartBeat = 10000;

        private readonly string localName;

        private IRtpMidiEventHandler rtpMidiEventHandler;

        private readonly IRtpMidiDeviceConnectionListener deviceConnectionListener;

        private IRtpMidiExceptionListener exceptionListener;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="sessionName">the name of this session</param>
        /// <param name="listenPort">UDP control port(0-65534)</param>
        /// <param name="deviceConnectionListener">the listener for RTP MIDI device connection</param>
        public RtpMidiSession(string sessionName, int listenPort, IRtpMidiDeviceConnectionListener deviceConnectionListener)
        {
            Port = listenPort;
            localName = sessionName;

            appleMidiParser = new AppleMidiParser(this);
            this.deviceConnectionListener = deviceConnectionListener;
        }

        /// <summary>
        /// Sets an event listener for RTP MIDI
        /// </summary>
        /// <param name="eventHandler">the RTP MIDI event callback</param>
        public void SetMidiEventListener(IRtpMidiEventHandler eventHandler)
        {
            rtpMidiEventHandler = eventHandler;
        }

        /// <summary>
        /// Sets an exception event listener for RTP MIDI
        /// </summary>
        /// <param name="listener">the RTP MIDI exception callback</param>
        public void SetRtpMidiExceptionListener(IRtpMidiExceptionListener listener)
        {
            exceptionListener = listener;
        }

        /// <summary>
        /// Start to connect with an another endpoint
        /// </summary>
        /// <param name="endPoint">IP endpoint</param>
        /// <returns></returns>
        public bool SendInvite(IPEndPoint endPoint)
        {
            lock (participants)
            {
                if (participants.Count >= MaxParticipants)
                {
                    return false;
                }

                var participant = new RtpMidiParticipant(this, endPoint)
                {
                    kind = ParticipantKind.Initiator,
                    lastInviteSentTime = RtpMidiClock.Ticks() - 1000,
                    lastSyncExchangeTime = RtpMidiClock.Ticks(),
                    initiatorToken = random.Next(),
                };

                participants.Add(participant);
            }

            return true;
        }

        /// <summary>
        /// Obtains DeviceId from specified participant
        /// </summary>
        /// <param name="participant">the participant</param>
        /// <returns></returns>
        private string GetDeviceId(RtpMidiParticipant participant)
        {
            return $"RtpMidi:{Port}:{participant.ssrc}";
        }

        /// <summary>
        /// Obtains the listening port number(0-65535) of session, from deviceId
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns>the listening port number(0-65535), or -1 if fails</returns>
        public static int GetPortFromDeviceId(string deviceId)
        {
            var deviceInfo = deviceId.Split(':');
            if (deviceInfo.Length != 3)
            {
                return -1;
            }

            return Convert.ToInt32(deviceInfo[1]);
        }

        /// <summary>
        /// Obtains the ssrc of participant, from deviceId
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns>the listening port number(0-65535), or -1 if fails</returns>
        public static int? GetSsrcFromDeviceId(string deviceId)
        {
            var deviceInfo = deviceId.Split(':');
            if (deviceInfo.Length != 3)
            {
                return null;
            }

            return Convert.ToInt32(deviceInfo[2]);
        }

        /// <summary>
        /// Obtains the participant, from deviceId
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns>the listening port number(0-65535), or -1 if fails</returns>
        public RtpMidiParticipant GetParticipantFromDeviceId(string deviceId)
        {
            var deviceInfo = deviceId.Split(':');
            if (deviceInfo.Length != 3)
            {
                return null;
            }

            return GetParticipantBySsrc(Convert.ToInt32(deviceInfo[2]));
        }

        /// <summary>
        /// Stops all RTP MIDI connection
        /// </summary>
        private void SendEndSession()
        {
            lock (participants)
            {
                foreach (var participant in participants)
                {
                    SendEndSession(participant);
                }
            }
        }

        private void SendEndSession(RtpMidiParticipant participant)
        {
            var endSession = new RtpMidiEndSession(0, Ssrc);
            WriteEndSession(participant.ControlEndPoint, endSession);
        }

        private void SendSynchronization(RtpMidiParticipant participant)
        {
            var synchronization = new RtpMidiSynchronization
            {
                Timestamps = new []
                {
                    rtpMidiClock.Now(),
                    0L,
                    0L,
                },
                Count = 0,
            };

            WriteSynchronization(participant.DataEndPoint, synchronization);

            participant.Synchronizing = true;
            participant.synchronizationCount++;
            participant.lastInviteSentTime = RtpMidiClock.Ticks();
        }

        /// <summary>
        /// Starts to communicate
        /// </summary>
        public void Begin()
        {
            Ssrc = random.Next();

            controlPort = new UdpClient(Port);
            dataPort = new UdpClient(Port + 1);

            lock (participants)
            {
                rtpMidiClock.Init(RtpMidiClock.MidiSamplingRateDefault);
            }
        }

        /// <summary>
        /// Disposes connections
        /// </summary>
        public void End()
        {
            SendEndSession();

#if ENABLE_RTP_MIDI_JOURNAL
            lock (participants)
            {
                foreach (var participant in participants)
                {
                    ClearIndefiniteArtifacts(participant);
                }

                participants.Clear();
            }
#endif

            controlPort?.Dispose();
            controlPort = null;
            dataPort?.Dispose();
            dataPort = null;
        }

        /// <summary>
        /// Starts to transmit
        /// </summary>
        /// <returns></returns>
        public bool BeginTransmission(RtpMidiParticipant participant)
        {
            if (dataPort == null)
            {
                return false;
            }

            lock (participant.outMidiBuffer)
            {
                if (participant.outMidiBuffer.Count > 0)
                {
                    if (participant.outMidiBuffer.Count + 2 > RtpMidiParticipant.MaxBufferSize)
                    {
                        WriteRtpMidiBuffer(participant);
                        participant.outMidiBuffer.Clear();
                    }
                    else
                    {
                        participant.outMidiBuffer.AddLast(0x00); // zero timestamp
                    }
                }
            }

            var dataPortConnected = dataPort.Client.Connected;
            lock (participants)
            {
                return dataPortConnected && participants.Count > 0;
            }
        }

        /// <summary>
        /// Ends transmission
        /// </summary>
        public void EndTransmission(RtpMidiParticipant participant)
        {
            // do nothing
        }

        /// <summary>
        /// Obtains available buffer size
        /// </summary>
        /// <returns>available buffer size</returns>
        public int Available(RtpMidiParticipant participant)
        {
            lock (participant.outMidiBuffer)
            {
                if (participant.outMidiBuffer.Count > 0)
                {
                    WriteRtpMidi(participant);
                }
            }

            lock (participant.inMidiBuffer)
            {
                if (participant.inMidiBuffer.Count > 0)
                {
                    return participant.inMidiBuffer.Count;
                }
            }

            lock (participant.dataBuffer)
            {
                if (participant.dataBuffer.Count > 0)
                {
                    ParseDataPackets(participant);
                }
            }

            return 0;
        }

        /// <summary>
        /// Read a byte from input buffer
        /// </summary>
        /// <returns></returns>
        public byte Read(RtpMidiParticipant participant)
        {
            lock (participant.inMidiBuffer)
            {
                var result = participant.inMidiBuffer.First?.Value;
                if (!result.HasValue)
                {
                    return 0;
                }

                participant.inMidiBuffer.RemoveFirst();
                return result.Value;
            }
        }

        internal void ReadDataPackets()
        {
            if (dataPort == null)
            {
                return;
            }

            var packetSize = dataPort.Available;
            while (packetSize > 0)
            {
                IPEndPoint receivedEndpoint = null;
                var received = dataPort.Receive(ref receivedEndpoint);
                dataReceivedEndPoint = receivedEndpoint; 
                packetSize -= received.Length;

                foreach (var participant in participants)
                {
                    if (participant.dataBuffer.Count > RtpMidiParticipant.MaxBufferSize)
                    {
                        exceptionListener?.OnError(RtpMidiExceptionKind.BufferFullException);
                        continue;
                    }

                    foreach (var b in received)
                    {
                        participant.dataBuffer.AddLast(b);
                    }
                }
            }
        }

        private void ParseDataPackets(RtpMidiParticipant participant)
        {
            while (participant.dataBuffer.Count > 0)
            {
                var retVal1 = participant.rtpMidiParser.Parse(participant, participant.dataBuffer);
                if (retVal1 == ParserResult.Processed || retVal1 == ParserResult.NotEnoughData)
                {
                    break;
                }

                var retVal2 = appleMidiParser.Parse(participant.dataBuffer, PortType.Data);
                if (retVal2 == ParserResult.Processed || retVal2 == ParserResult.NotEnoughData)
                {
                    break;
                }

                // one or the other don't have enough data to determine the protocol
                if (retVal1 == ParserResult.NotSureGiveMeMoreData || retVal2 == ParserResult.NotSureGiveMeMoreData)
                {
                    break;
                }

                participant.dataBuffer.RemoveFirst();

                exceptionListener?.OnError(RtpMidiExceptionKind.UnexpectedParseException);
            }
        }

        internal int ReadControlPackets()
        {
            if (controlPort == null)
            {
                return 0;
            }

            var packetSize = controlPort.Available;
            while (packetSize > 0 && controlBuffer.Count < RtpMidiParticipant.MaxBufferSize)
            {
                IPEndPoint receivedEndpoint = null;
                var received = controlPort.Receive(ref receivedEndpoint);
                controlReceivedEndPoint = receivedEndpoint;
                packetSize -= received.Length;

                foreach (var b in received)
                {
                    controlBuffer.AddLast(b);
                }
            }

            return controlBuffer.Count;
        }

        internal void ParseControlPackets()
        {
            while (controlBuffer.Count > 0)
            {
                var retVal = appleMidiParser.Parse(controlBuffer, PortType.Control);
                if (retVal == ParserResult.Processed || retVal == ParserResult.NotEnoughData || retVal == ParserResult.NotSureGiveMeMoreData)
                {
                    break;
                }
                if (retVal == ParserResult.UnexpectedData)
                {
                    controlBuffer.RemoveFirst();
                    exceptionListener?.OnError(RtpMidiExceptionKind.ParseException);
                }
                else if (retVal == ParserResult.SessionNameVeryLong)
                {
                    // purge the rest of the data in controlPort
                    controlBuffer.Clear();
                }
            }
        }
        
        /// <summary>
        /// Notify receiving connection invitation
        /// </summary>
        /// <param name="invitation">the invitation</param>
        /// <param name="portType">control or data</param>
        public void ReceivedInvitation(RtpMidiInvitation invitation, PortType portType)
        {
            if (portType == PortType.Control)
            {
                ReceivedControlInvitation(invitation);
            }
            else
            {
                ReceivedDataInvitation(invitation);
            }
        }

        private void ReceivedControlInvitation(RtpMidiInvitation invitation)
        {
            // ignore invitation of a participant already in the participant list
            if (GetParticipantBySsrc(invitation.Ssrc) != null)
            {
                return;
            }

            if (controlReceivedEndPoint == null)
            {
                return;
            }

            lock (participants)
            {
                if (participants.Count >= MaxParticipants)
                {
                    WriteInvitation(controlPort, controlReceivedEndPoint, invitation, RtpMidiConstants.InvitationRejected);
                    exceptionListener?.OnError(RtpMidiExceptionKind.TooManyParticipantsException);
                }

                var participant = new RtpMidiParticipant(this, controlReceivedEndPoint)
                {
                    kind = ParticipantKind.Listener,
                    ssrc = invitation.Ssrc,
                    lastSyncExchangeTime = RtpMidiClock.Ticks(),
                    sessionName = invitation.SessionName,
                };

                // Re-use the invitation for acceptance. Overwrite sessionName with ours
                var invitationAccepted = new RtpMidiInvitation
                {
                    SessionName = localName,
                    InitiatorToken = invitation.InitiatorToken,
                };

                participants.Add(participant);

                WriteInvitation(controlPort, participant.ControlEndPoint, invitationAccepted, RtpMidiConstants.InvitationAccepted);
            }
        }

        private RtpMidiParticipant GetParticipantBySsrc(int ssrc)
        {
            lock (participants)
            {
                foreach (var participant in participants)
                {
                    if (participant.ssrc == ssrc)
                    {
                        return participant;
                    }
                }
            }

            return null;
        }

        private void ReceivedDataInvitation(RtpMidiInvitation invitation)
        {
            var participant = GetParticipantBySsrc(invitation.Ssrc);
            if (participant == null)
            {
                if (dataReceivedEndPoint != null)
                {
                    WriteInvitation(dataPort, dataReceivedEndPoint, invitation, RtpMidiConstants.InvitationRejected);
                }
                exceptionListener?.OnError(RtpMidiExceptionKind.ParticipantNotFoundException);
                return;
            }
            
            var invitationAccepted = new RtpMidiInvitation
            {
                SessionName = localName,
                InitiatorToken = invitation.InitiatorToken,
            };

            WriteInvitation(dataPort, participant.DataEndPoint, invitationAccepted, RtpMidiConstants.InvitationAccepted);
                
            deviceConnectionListener.OnRtpMidiDeviceAttached(GetDeviceId(participant));

            participant.kind = ParticipantKind.Listener;
        }

        /// <summary>
        /// Write single MIDI byte to output buffer
        /// </summary>
        /// <param name="participant">the participant</param>
        /// <param name="datum">the MIDI data byte</param>
        public void Write(RtpMidiParticipant participant, byte datum)
        {
            lock (participant.outMidiBuffer)
            {
                // do we still have place in the buffer for 1 more character?
                if ((participant.outMidiBuffer.Count) + 2 > RtpMidiParticipant.MaxBufferSize)
                {
                    // buffer is almost full, only 1 more character
                    if ((byte)MidiType.SystemExclusive == participant.outMidiBuffer.First?.Value)
                    {
                        // Add Sysex at the end of this partial SysEx (in the last available slot) ...
                        participant.outMidiBuffer.AddLast((byte)MidiType.SystemExclusiveStart);

                        WriteRtpMidi(participant);
                        // and start again with a fresh continuation of
                        // a next SysEx block.
                        participant.outMidiBuffer.Clear();
                        participant.outMidiBuffer.AddLast((byte)MidiType.SystemExclusiveEnd);
                    }
                    else
                    {
                        exceptionListener?.OnError(RtpMidiExceptionKind.BufferFullException);
                    }
                }

                // store in local buffer, as we do *not* know the length of the message prior to sending
                participant.outMidiBuffer.AddLast(datum);
            }
        }

        private void WriteInvitation(UdpClient udpClient, IPEndPoint endPoint, RtpMidiInvitation invitation, byte[] command)
        {
            var dataStream = new MemoryStream();

            invitation.Ssrc = Ssrc;

            dataStream.Write(RtpMidiConstants.Signature, 0, 2);
            dataStream.Write(command, 0, 2);
            dataStream.Write(RtpMidiConstants.ProtocolVersion, 0, 4);
            dataStream.Write(
                new[]
                {
                    (byte)((invitation.InitiatorToken >> 24) & 0xff),
                    (byte)((invitation.InitiatorToken >> 16) & 0xff),
                    (byte)((invitation.InitiatorToken >> 8) & 0xff),
                    (byte)(invitation.InitiatorToken & 0xff),
                    (byte)((invitation.Ssrc >> 24) & 0xff),
                    (byte)((invitation.Ssrc >> 16) & 0xff),
                    (byte)((invitation.Ssrc >> 8) & 0xff),
                    (byte)(invitation.Ssrc & 0xff),
                }
                , 0, 8);

            udpClient?.Send(dataStream.ToArray(), (int)dataStream.Length, endPoint);
        }

        private void WriteReceiverFeedback(IPEndPoint endPoint, RtpMidiReceiverFeedback receiverFeedback)
        {
            var dataStream = new MemoryStream();

            dataStream.Write(RtpMidiConstants.Signature, 0, 2);
            dataStream.Write(RtpMidiConstants.ReceiverFeedback, 0, 2);
            dataStream.Write(
                new[]
                {
                    (byte)((receiverFeedback.Ssrc >> 24) & 0xff),
                    (byte)((receiverFeedback.Ssrc >> 16) & 0xff),
                    (byte)((receiverFeedback.Ssrc >> 8) & 0xff),
                    (byte)(receiverFeedback.Ssrc & 0xff),
                    (byte)((receiverFeedback.SequenceNr >> 8) & 0xff),
                    (byte)(receiverFeedback.SequenceNr & 0xff),
                    (byte)0,
                    (byte)0,
                }
            ,0, 8);

            controlPort?.Send(dataStream.ToArray(), (int)dataStream.Length, endPoint);
        }

        private void WriteSynchronization(IPEndPoint endPoint, RtpMidiSynchronization synchronization)
        {
            var dataStream = new MemoryStream();

            synchronization.Ssrc = Ssrc;

            dataStream.Write(RtpMidiConstants.Signature, 0, 2);
            dataStream.Write(RtpMidiConstants.Synchronization, 0, 2);
            dataStream.Write(
                new[]
                {
                    (byte)((synchronization.Ssrc >> 24) & 0xff),
                    (byte)((synchronization.Ssrc >> 16) & 0xff),
                    (byte)((synchronization.Ssrc >> 8) & 0xff),
                    (byte)(synchronization.Ssrc & 0xff),
                    (byte)(synchronization.Count & 0xff),
                    (byte)0,
                    (byte)0,
                    (byte)0,
                    (byte)((synchronization.Timestamps[0] >> 56) & 0xff),
                    (byte)((synchronization.Timestamps[0] >> 48) & 0xff),
                    (byte)((synchronization.Timestamps[0] >> 40) & 0xff),
                    (byte)((synchronization.Timestamps[0] >> 32) & 0xff),
                    (byte)((synchronization.Timestamps[0] >> 24) & 0xff),
                    (byte)((synchronization.Timestamps[0] >> 16) & 0xff),
                    (byte)((synchronization.Timestamps[0] >> 8) & 0xff),
                    (byte)(synchronization.Timestamps[0] & 0xff),
                    (byte)((synchronization.Timestamps[1] >> 56) & 0xff),
                    (byte)((synchronization.Timestamps[1] >> 48) & 0xff),
                    (byte)((synchronization.Timestamps[1] >> 40) & 0xff),
                    (byte)((synchronization.Timestamps[1] >> 32) & 0xff),
                    (byte)((synchronization.Timestamps[1] >> 24) & 0xff),
                    (byte)((synchronization.Timestamps[1] >> 16) & 0xff),
                    (byte)((synchronization.Timestamps[1] >> 8) & 0xff),
                    (byte)(synchronization.Timestamps[1] & 0xff),
                    (byte)((synchronization.Timestamps[2] >> 56) & 0xff),
                    (byte)((synchronization.Timestamps[2] >> 48) & 0xff),
                    (byte)((synchronization.Timestamps[2] >> 40) & 0xff),
                    (byte)((synchronization.Timestamps[2] >> 32) & 0xff),
                    (byte)((synchronization.Timestamps[2] >> 24) & 0xff),
                    (byte)((synchronization.Timestamps[2] >> 16) & 0xff),
                    (byte)((synchronization.Timestamps[2] >> 8) & 0xff),
                    (byte)(synchronization.Timestamps[2] & 0xff),
                }
            ,0, 32);

            dataPort?.Send(dataStream.ToArray(), (int)dataStream.Length, endPoint);
        }

        private void WriteEndSession(IPEndPoint controlEndPoint, RtpMidiEndSession endSession)
        {
            var dataStream = new MemoryStream();

            dataStream.Write(RtpMidiConstants.Signature, 0, 2);
            dataStream.Write(RtpMidiConstants.EndSession, 0, 2);
            dataStream.Write(RtpMidiConstants.ProtocolVersion, 0, 4);
            dataStream.Write(
                new[]
                {
                    (byte)((endSession.InitiatorToken >> 24) & 0xff),
                    (byte)((endSession.InitiatorToken >> 16) & 0xff),
                    (byte)((endSession.InitiatorToken >> 8) & 0xff),
                    (byte)(endSession.InitiatorToken & 0xff),
                    (byte)((endSession.Ssrc >> 24) & 0xff),
                    (byte)((endSession.Ssrc >> 16) & 0xff),
                    (byte)((endSession.Ssrc >> 8) & 0xff),
                    (byte)(endSession.Ssrc & 0xff),
                }
            ,0, 8);

            controlPort?.Send(dataStream.ToArray(), (int)dataStream.Length, controlEndPoint);
        }

        private void WriteRtpMidi(RtpMidiParticipant participant)
        {
            lock (participant.outMidiBuffer)
            {
                WriteRtpMidiBuffer(participant);
                participant.outMidiBuffer.Clear();
            }
        }

        private void WriteRtpMidiBuffer(RtpMidiParticipant participant)
        {
            var dataStream = new MemoryStream();

            var rtp = new Rtp
            {
                // First octet
                vpxcc = 2 << 6,
                // second octet
                mpayload = 97,
                ssrc = Ssrc,
                timestamp = (int)rtpMidiClock.Now(),
            };

            // increment the sequenceNr
            participant.sendSequenceNrExtended++;
            var packetSequenceI = participant.sendSequenceNr;

            rtp.sequenceNr = packetSequenceI;

            // write rtp header
            dataStream.Write(new[]
            {
                rtp.vpxcc,
                rtp.mpayload,
                (byte)((rtp.sequenceNr >> 8) & 0xff),
                (byte)(rtp.sequenceNr & 0xff),
                (byte)((rtp.timestamp >> 24) & 0xff),
                (byte)((rtp.timestamp >> 16) & 0xff),
                (byte)((rtp.timestamp >> 8) & 0xff),
                (byte)(rtp.timestamp & 0xff),
                (byte)((rtp.ssrc >> 24) & 0xff),
                (byte)((rtp.ssrc >> 16) & 0xff),
                (byte)((rtp.ssrc >> 8) & 0xff),
                (byte)(rtp.ssrc & 0xff),
            }, 0, 12);

            // Write rtpMIDI section. Journal is not counted in MaxBufferSize.
            var bufferLen = participant.outMidiBuffer.Count;
            byte rtpMidiFlags = 0;
            if (bufferLen < 0x0f)
            {
                rtpMidiFlags = (byte)bufferLen;
            }
            else
            {
                rtpMidiFlags = (byte)(0x80 | (bufferLen >> 8));
            }

#if ENABLE_RTP_MIDI_JOURNAL
            rtpMidiFlags |= 0x40;
            var checkpointC = SelectSendCheckpoint(participant, packetSequenceI);
            var journalData = participant.journal.Encode(packetSequenceI, checkpointC);
#endif

            if (bufferLen < 0x0f)
            {
                dataStream.Write(new[] {rtpMidiFlags}, 0, 1);
            }
            else
            {
                dataStream.Write(new[] {rtpMidiFlags, (byte)bufferLen}, 0, 2);
            }

            // write out the MIDI Section
            var outMidiArray = new byte[bufferLen];
            participant.outMidiBuffer.CopyTo(outMidiArray, 0);
            dataStream.Write(outMidiArray, 0, bufferLen);

#if ENABLE_RTP_MIDI_JOURNAL
            dataStream.Write(journalData, 0, journalData.Length);
            // Journal for I excludes this packet's MIDI; assign pending commands to I after Encode.
            participant.journal.CommitPending(packetSequenceI);
            participant.journal.PruneBefore(checkpointC);
            if (!participant.hasSentRtpMidi)
            {
                participant.firstSentSequenceExtended = participant.sendSequenceNrExtended;
            }

            participant.hasSentRtpMidi = true;
            participant.lastRtpMidiSendTime = RtpMidiClock.Ticks();
#endif

            dataPort?.Send(dataStream.ToArray(), (int)dataStream.Length, participant.DataEndPoint);
        }

        internal void ManageSessionInvites()
        {
            // (Initiators only)
            lock (participants)
            {
                try
                {
                    foreach (var participant in participants)
                    {
                        if (participant.kind == ParticipantKind.Listener)
                        {
                            continue;
                        }

                        if (participant.invitationStatus == InviteStatus.DataInvitationAccepted)
                        {
                            participant.invitationStatus = InviteStatus.Connected;
                        }

                        if (participant.invitationStatus == InviteStatus.Connected)
                        {
                            continue;
                        }

                        // try to connect every 1 second (1000 ms)
                        if (RtpMidiClock.Ticks() - participant.lastInviteSentTime > 1000)
                        {
                            if (participant.connectionAttempts >= MaxSessionInvitesAttempts)
                            {
                                // After too many attempts, stop.
                                SendEndSession(participant);

                                participantsToRemove.Add(participant);
                                exceptionListener?.OnError(RtpMidiExceptionKind.NoResponseFromConnectionRequestException);
                                continue;
                            }

                            participant.lastInviteSentTime = RtpMidiClock.Ticks();
                            participant.connectionAttempts++;

                            var invitation = new RtpMidiInvitation();
                            invitation.Ssrc = Ssrc;
                            invitation.InitiatorToken = participant.initiatorToken;
                            invitation.SessionName = localName;

                            if (participant.invitationStatus == InviteStatus.Initiating ||
                                participant.invitationStatus == InviteStatus.AwaitingControlInvitationAccepted)
                            {
                                WriteInvitation(controlPort, participant.ControlEndPoint, invitation, RtpMidiConstants.Invitation);
                                participant.invitationStatus = InviteStatus.AwaitingControlInvitationAccepted;
                            }
                            else if (participant.invitationStatus == InviteStatus.ControlInvitationAccepted ||
                                     participant.invitationStatus == InviteStatus.AwaitingDataInvitationAccepted)
                            {
                                WriteInvitation(dataPort, participant.DataEndPoint, invitation, RtpMidiConstants.Invitation);
                                participant.invitationStatus = InviteStatus.AwaitingDataInvitationAccepted;
                            }
                        }
                    }
                }
                finally
                {
                    foreach (var participant in participantsToRemove)
                    {
                        RemoveParticipant(participant);
                    }
                    participantsToRemove.Clear();
                }
            }
        }

        internal void ManageReceiverFeedback()
        {
            foreach (var participant in participants)
            {
                if (participant.ssrc == 0 || !participant.hasReceivedRtp)
                {
                    continue;
                }

                if (RtpMidiClock.Ticks() - participant.lastReceiverFeedbackSentTime < ReceiversFeedbackThreshold)
                {
                    continue;
                }

                var rf = new RtpMidiReceiverFeedback
                {
                    Ssrc = Ssrc,
                    // AppleMIDI RS carries the low 16 bits of the extended receive sequence.
                    SequenceNr = participant.receiveSequenceNr,
                };
                WriteReceiverFeedback(participant.ControlEndPoint, rf);
                participant.lastReceiverFeedbackSentTime = RtpMidiClock.Ticks();
            }
        }

#if ENABLE_RTP_MIDI_JOURNAL
        /// <summary>
        /// Sends MIDI LEN=0 recovery-journal packets so a trailing loss can still be repaired.
        /// </summary>
        internal void ManageTrailingLoss()
        {
            lock (participants)
            {
                foreach (var participant in participants)
                {
                    if (participant.ssrc == 0 || participant.invitationStatus != InviteStatus.Connected)
                    {
                        continue;
                    }

                    if (!participant.hasSentRtpMidi || participant.journal.HistoryCount == 0)
                    {
                        continue;
                    }

                    lock (participant.outMidiBuffer)
                    {
                        if (participant.outMidiBuffer.Count > 0)
                        {
                            continue;
                        }
                    }

                    // Stop once the peer's RS (extended M(k)) has caught up to our last sent sequence.
                    if (participant.hasRemoteFeedback &&
                        participant.remoteFeedbackExtended >= participant.sendSequenceNrExtended)
                    {
                        continue;
                    }

                    if (RtpMidiClock.Ticks() - participant.lastRtpMidiSendTime < TrailingLossIntervalMs)
                    {
                        continue;
                    }

                    WriteRtpMidi(participant);
                }
            }
        }
#endif

        internal void ManageSynchronization()
        {
            lock (participants)
            {
                try
                {
                    foreach (var participant in participants)
                    {
                        if (participant.ssrc == 0)
                        {
                            continue;
                        }

                        if (participant.invitationStatus != InviteStatus.Connected)
                        {
                            continue;
                        }

#if ENABLE_RTP_MIDI_JOURNAL
                        if (ShouldDisconnectForJournalOverflow(participant))
                        {
                            SendEndSession(participant);
                            participantsToRemove.Add(participant);
                            exceptionListener?.OnError(RtpMidiExceptionKind.RecoveryJournalOverflowException);
                            continue;
                        }
#endif

                        if (participant.kind == ParticipantKind.Listener)
                        {
                            if (RtpMidiClock.Ticks() - participant.lastSyncExchangeTime > CkMaxTimeOut)
                            {
                                SendEndSession(participant);
                                participantsToRemove.Add(participant);
                                exceptionListener?.OnError(RtpMidiExceptionKind.ListenerTimeOutException);
                            }
                        }
                        else
                        {
                            if (participant.Synchronizing)
                            {
                                if (ManageSynchronizationInitiatorInvites(participant))
                                {
                                    participantsToRemove.Add(participant);
                                    exceptionListener?.OnError(RtpMidiExceptionKind.MaxAttemptsException);
                                }
                            }
                            else
                            {
                                ManageSynchronizationInitiatorHeartBeat(participant);
                            }
                        }
                    }
                }
                finally
                {
                    foreach (var participant in participantsToRemove)
                    {
                        RemoveParticipant(participant);
                    }
                    participantsToRemove.Clear();
                }
            }
        }

        private bool ManageSynchronizationInitiatorInvites(RtpMidiParticipant participant)
        {
            if (RtpMidiClock.Ticks() - participant.lastInviteSentTime > 10000)
            {
                if (participant.synchronizationCount > MaxSynchronizationCk0Attempts)
                {
                    SendEndSession(participant);
                    return true;
                }

                SendSynchronization(participant);
            }

            return false;
        }

        private void ManageSynchronizationInitiatorHeartBeat(RtpMidiParticipant participant)
        {
            var doSynchronize = false;
            if (participant.synchronizationHeartBeats < 2)
            {
                if (RtpMidiClock.Ticks() - participant.lastInviteSentTime > 500)
                {
                    participant.synchronizationHeartBeats++;
                    doSynchronize = true;
                }
            }
            else if (participant.synchronizationHeartBeats < 7)
            {
                if (RtpMidiClock.Ticks() - participant.lastInviteSentTime > 1500)
                {
                    participant.synchronizationHeartBeats++;
                    doSynchronize = true;
                }
            }
            else if (RtpMidiClock.Ticks() - participant.lastInviteSentTime > SynchronizationHeartBeat)
            {
                doSynchronize = true;
            }

            if (!doSynchronize)
            {
                return;
            }

            participant.synchronizationCount = 0;
            SendSynchronization(participant);
        }
        public void ReceivedEndSession(RtpMidiEndSession endSession)
        {
            var participant = GetParticipantBySsrc(endSession.Ssrc);
            if (participant != null)
            {
                lock (participants)
                {
                    RemoveParticipant(participant);
                }
            }
        }

        /// <summary>
        /// Notify receiving connection synchronization
        /// </summary>
        /// <param name="synchronization">the synchronization</param>
        public void ReceivedSynchronization(RtpMidiSynchronization synchronization)
        {
            var participant = GetParticipantBySsrc(synchronization.Ssrc);
            if (participant == null)
            {
                exceptionListener?.OnError(RtpMidiExceptionKind.ParticipantNotFoundException);
                return;
            }
            
            switch (synchronization.Count)
            {
                case 0:
                    // From session initiator
                    lock (participants)
                    {
                        synchronization.Timestamps[1] = rtpMidiClock.Now();
                    }
                    synchronization.Count = 1;
                    WriteSynchronization(participant.DataEndPoint, synchronization);
                    break;
                case 1:
                    // From session listener
                    lock (participants)
                    {
                        synchronization.Timestamps[2] = rtpMidiClock.Now();
                    }
                    synchronization.Count = 2;
                    WriteSynchronization(participant.DataEndPoint, synchronization);
                    participant.Synchronizing = false;
                    break;
                case 2:
                    participant.OffsetEstimate = (synchronization.Timestamps[2] + synchronization.Timestamps[0]) / 2 - synchronization.Timestamps[1];
                    break;
            }

            // All participants need to check in regularly,
            // failing to do so will result in a lost connection.
            participant.lastSyncExchangeTime = RtpMidiClock.Ticks();
        }

        /// <summary>
        /// Notify receiving connection invitation accepted
        /// </summary>
        /// <param name="invitationAccepted">the invitation accepted</param>
        /// <param name="portType">the type of port</param>
        public void ReceivedInvitationAccepted(RtpMidiInvitationAccepted invitationAccepted, PortType portType)
        {

            if (portType == PortType.Control)
            {
                ReceivedControlInvitationAccepted(invitationAccepted);
            }
            else
            {
                ReceivedDataInvitationAccepted(invitationAccepted);
            }
        }

        private void ReceivedControlInvitationAccepted(RtpMidiInvitationAccepted invitationAccepted)
        {
            var participant = GetParticipantByInitiatorToken(invitationAccepted.InitiatorToken);
            if (participant == null)
            {
                return;
            }

            participant.ssrc = invitationAccepted.Ssrc;
            participant.lastInviteSentTime = RtpMidiClock.Ticks() - 1000;
            participant.connectionAttempts = 0;
            participant.invitationStatus = InviteStatus.ControlInvitationAccepted;
            participant.sessionName = invitationAccepted.SessionName;
        }

        private void ReceivedDataInvitationAccepted(RtpMidiInvitationAccepted invitationAccepted)
        {
            var participant = GetParticipantByInitiatorToken(invitationAccepted.InitiatorToken);
            if (participant == null)
            {
                return;
            }

            participant.invitationStatus = InviteStatus.DataInvitationAccepted;

            deviceConnectionListener.OnRtpMidiDeviceAttached(GetDeviceId(participant));
        }

        private RtpMidiParticipant GetParticipantByInitiatorToken(int initiatorToken)
        {
            lock (participants)
            {
                foreach (var participant in participants)
                {
                    if (participant.initiatorToken == initiatorToken)
                    {
                        return participant;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Notify receiving connection invitation rejected
        /// </summary>
        /// <param name="invitationRejected">the invitation rejected</param>
        public void ReceivedInvitationRejected(RtpMidiInvitationRejected invitationRejected)
        {
            var participant = GetParticipantBySsrc(invitationRejected.Ssrc);
            if (participant != null)
            {
                lock (participants)
                {
                    RemoveParticipant(participant);
                }
            }
        }

        /// <summary>
        /// Notify receiving connection receiver feedback
        /// </summary>
        /// <param name="receiverFeedback">the receiver feedback</param>
        public void ReceivedReceiverFeedback(RtpMidiReceiverFeedback receiverFeedback)
        {
            var participant = GetParticipantBySsrc(receiverFeedback.Ssrc);
            if (participant == null)
            {
                exceptionListener?.OnError(RtpMidiExceptionKind.ParticipantNotFoundException);
                return;
            }

#if ENABLE_RTP_MIDI_JOURNAL
            var previous = participant.hasRemoteFeedback
                ? participant.remoteFeedbackExtended
                : participant.sendSequenceNrExtended;
            participant.remoteFeedbackExtended = RtpMidiCheckpointPolicy.ExtendReceiverFeedback(
                previous,
                receiverFeedback.SequenceNr);
            participant.remoteFeedbackSequenceNr = RtpSequenceNumber.Low16(participant.remoteFeedbackExtended);
            participant.hasRemoteFeedback = true;
#endif

            if (RtpSequenceNumber.IsAheadOf(receiverFeedback.SequenceNr, participant.sendSequenceNr))
            {
                exceptionListener?.OnError(RtpMidiExceptionKind.SendPacketsDropped);
            }
        }

        /// <summary>
        /// Notify receiving connection bitrate receive limit
        /// </summary>
        /// <param name="bitrateReceiveLimit">the bitrate receive limit</param>
        public void ReceivedBitrateReceiveLimit(RtpMidiBitrateReceiveLimit bitrateReceiveLimit)
        {
            // do nothing
        }

        /// <summary>
        /// Receive single byte MIDI message
        /// </summary>
        public void ReceivedMidi(byte midi)
        {
            lock (participants)
            {
                foreach (var participant in participants)
                {
                    participant.inMidiBuffer.AddLast(midi);
                }
            }
        }

        /// <summary>
        /// Process received MIDI messages
        /// </summary>
        public void ReceivedMidi(RtpMidiParticipant participant, MidiType midiType, byte[] data)
        {
            switch (midiType)
            {
                case MidiType.NoteOff:
                    rtpMidiEventHandler?.OnMidiNoteOff(GetDeviceId(participant), data[0] & 0xf, data[1], data[2]);
                    break;
                case MidiType.NoteOn:
                    rtpMidiEventHandler?.OnMidiNoteOn(GetDeviceId(participant), data[0] & 0xf, data[1], data[2]);
                    break;
                case MidiType.AfterTouchPoly:
                    rtpMidiEventHandler?.OnMidiPolyphonicAftertouch(GetDeviceId(participant), data[0] & 0xf, data[1], data[2]);
                    break;
                case MidiType.ControlChange:
                    rtpMidiEventHandler?.OnMidiControlChange(GetDeviceId(participant), data[0] & 0xf, data[1], data[2]);
                    break;
                case MidiType.ProgramChange:
                    rtpMidiEventHandler?.OnMidiProgramChange(GetDeviceId(participant), data[0] & 0xf, data[1]);
                    break;
                case MidiType.AfterTouchChannel:
                    rtpMidiEventHandler?.OnMidiChannelAftertouch(GetDeviceId(participant), data[0] & 0xf, data[1]);
                    break;
                case MidiType.PitchBend:
                    rtpMidiEventHandler?.OnMidiPitchWheel(GetDeviceId(participant), data[0] & 0xf, data[1] | (data[2] << 7));
                    break;
                case MidiType.SystemExclusive:
                    rtpMidiEventHandler?.OnMidiSystemExclusive(GetDeviceId(participant), data);
                    break;
                case MidiType.TimeCodeQuarterFrame:
                    rtpMidiEventHandler?.OnMidiTimeCodeQuarterFrame(GetDeviceId(participant), data[0]);
                    break;
                case MidiType.SongPosition:
                    rtpMidiEventHandler?.OnMidiSongPositionPointer(GetDeviceId(participant), data[0] | (data[1] << 7));
                    break;
                case MidiType.SongSelect:
                    rtpMidiEventHandler?.OnMidiSongSelect(GetDeviceId(participant), data[0]);
                    break;
                case MidiType.TuneRequest:
                    rtpMidiEventHandler?.OnMidiTuneRequest(GetDeviceId(participant));
                    break;
                case MidiType.Clock:
                    rtpMidiEventHandler?.OnMidiTimingClock(GetDeviceId(participant));
                    break;
                case MidiType.Start:
                    rtpMidiEventHandler?.OnMidiStart(GetDeviceId(participant));
                    break;
                case MidiType.Continue:
                    rtpMidiEventHandler?.OnMidiContinue(GetDeviceId(participant));
                    break;
                case MidiType.Stop:
                    rtpMidiEventHandler?.OnMidiStop(GetDeviceId(participant));
                    break;
                case MidiType.ActiveSensing:
                    rtpMidiEventHandler?.OnMidiActiveSensing(GetDeviceId(participant));
                    break;
                case MidiType.SystemReset:
                    rtpMidiEventHandler?.OnMidiReset(GetDeviceId(participant));
                    break;
            }
        }

        /// <summary>
        /// Process received RTP data
        /// </summary>
        public void ReceivedRtp(Rtp rtp)
        {
            var participant = GetParticipantBySsrc(rtp.ssrc);
            if (participant == null)
            {
                return;
            }

#if ENABLE_RTP_MIDI_JOURNAL
            var kind = RtpMidiReceivePolicy.Classify(participant.FirstMessageReceived, participant.receiveSequenceNr, rtp.sequenceNr);
            participant.lastReceiveKind = kind;
            participant.playMidiCommands = RtpMidiReceivePolicy.ShouldPlayMidi(kind);
            participant.applyRecoveryJournal = RtpMidiReceivePolicy.ShouldApplyJournal(kind);

            if (kind == RtpPacketReceiveKind.Reordered)
            {
                participant.lostPacketCount = 0;
                participant.hasReceivedRtp = true;
                return;
            }

            if (participant.FirstMessageReceived)
            {
                participant.FirstMessageReceived = false;
                participant.receiveSequenceNrExtended = rtp.sequenceNr;
                participant.highestReceivedSequenceBeforePacket = rtp.sequenceNr;
                participant.lostPacketCount = 0;
            }
            else
            {
                var highestBefore = participant.receiveSequenceNr;
                participant.highestReceivedSequenceBeforePacket = highestBefore;
                participant.receiveSequenceNrExtended = RtpSequenceNumber.Extend(participant.receiveSequenceNrExtended, rtp.sequenceNr);
                var delta = RtpSequenceNumber.SerialDelta(highestBefore, rtp.sequenceNr);
                participant.lostPacketCount = delta > 1 ? delta - 1 : 0;
                if (participant.lostPacketCount > 0)
                {
                    exceptionListener?.OnError(RtpMidiExceptionKind.ReceivedPacketsDropped);
                }
            }

            participant.hasReceivedRtp = true;
#else
            if (participant.FirstMessageReceived)
            {
                // avoids first message to generate sequence exception
                // as we do not know the last sequenceNr received.
                participant.FirstMessageReceived = false;
                participant.receiveSequenceNrExtended = rtp.sequenceNr;
            }
            else
            {
                var previous = participant.receiveSequenceNr;
                participant.receiveSequenceNrExtended = RtpSequenceNumber.Extend(participant.receiveSequenceNrExtended, rtp.sequenceNr);
                var delta = RtpSequenceNumber.SerialDelta(previous, rtp.sequenceNr);
                participant.lostPacketCount = delta > 1 ? delta - 1 : 0;
                if (participant.lostPacketCount > 0)
                {
                    // Packet loss detected
                    // see C.2.2.2.  The closed-loop Sending Policy
                    exceptionListener?.OnError(RtpMidiExceptionKind.ReceivedPacketsDropped);
                }
            }

            participant.hasReceivedRtp = true;
#endif
        }

#if ENABLE_RTP_MIDI_JOURNAL
        private static ushort SelectSendCheckpoint(RtpMidiParticipant participant, ushort packetSequenceI)
        {
            var hasSessionStart = participant.journal.TryGetSessionStartSequence(out var sessionStart);
            return RtpMidiCheckpointPolicy.SelectCheckpoint(
                packetSequenceI,
                participant.hasRemoteFeedback,
                participant.remoteFeedbackExtended,
                hasSessionStart,
                sessionStart,
                participant.journal);
        }

        private static bool ShouldDisconnectForJournalOverflow(RtpMidiParticipant participant)
        {
            if (!participant.hasSentRtpMidi)
            {
                return false;
            }

            var span = RtpMidiCheckpointPolicy.UnackedPacketSpan(
                participant.sendSequenceNrExtended,
                participant.hasRemoteFeedback,
                participant.remoteFeedbackExtended,
                true,
                participant.firstSentSequenceExtended);

            return span > MaxUnackedJournalPackets;
        }

        /// <summary>
        /// Applies Chapter N / E when receive policy requires it.
        /// </summary>
        internal void ApplyRecoveryJournal(RtpMidiParticipant participant, byte[] journal, int length)
        {
            if (participant == null || journal == null || length < RtpMidiJournalSection.HeaderLength)
            {
                return;
            }

            if (participant.lastReceiveKind == RtpPacketReceiveKind.Loss)
            {
                var checkpoint = RtpMidiJournalSection.GetCheckpointPacketSeqnum(journal);
                if (!RtpMidiReceivePolicy.CheckpointCoversLoss(checkpoint, participant.highestReceivedSequenceBeforePacket))
                {
                    ClearIndefiniteArtifacts(participant);
                    return;
                }
            }

            if (!RtpMidiNoteJournal.TryDecode(journal, length, out var recovered))
            {
                return;
            }

            for (var i = 0; i < recovered.Count; i++)
            {
                EmitRecovered(participant, recovered[i]);
            }
        }

        private void EmitRecovered(RtpMidiParticipant participant, RecoveredMidi command)
        {
            if (rtpMidiEventHandler == null)
            {
                return;
            }

            var deviceId = GetDeviceId(participant);
            switch (command.Type)
            {
                case MidiType.NoteOff:
                    rtpMidiEventHandler.OnMidiNoteOff(deviceId, command.Channel, command.Data1, command.Data2);
                    break;
                case MidiType.NoteOn:
                    rtpMidiEventHandler.OnMidiNoteOn(deviceId, command.Channel, command.Data1, command.Data2);
                    break;
                case MidiType.ControlChange:
                    rtpMidiEventHandler.OnMidiControlChange(deviceId, command.Channel, command.Data1, command.Data2);
                    break;
                case MidiType.ProgramChange:
                    rtpMidiEventHandler.OnMidiProgramChange(deviceId, command.Channel, command.Data1);
                    break;
                case MidiType.AfterTouchPoly:
                    rtpMidiEventHandler.OnMidiPolyphonicAftertouch(deviceId, command.Channel, command.Data1, command.Data2);
                    break;
                case MidiType.AfterTouchChannel:
                    rtpMidiEventHandler.OnMidiChannelAftertouch(deviceId, command.Channel, command.Data1);
                    break;
                case MidiType.PitchBend:
                    rtpMidiEventHandler.OnMidiPitchWheel(deviceId, command.Channel, command.Data1 | (command.Data2 << 7));
                    break;
                case MidiType.SystemExclusive:
                    if (command.Payload != null)
                    {
                        rtpMidiEventHandler.OnMidiSystemExclusive(deviceId, command.Payload);
                    }

                    break;
                case MidiType.TimeCodeQuarterFrame:
                    rtpMidiEventHandler.OnMidiTimeCodeQuarterFrame(deviceId, command.Data1);
                    break;
                case MidiType.SongSelect:
                    rtpMidiEventHandler.OnMidiSongSelect(deviceId, command.Data1);
                    break;
                case MidiType.SongPosition:
                    rtpMidiEventHandler.OnMidiSongPositionPointer(deviceId, command.Data1 | (command.Data2 << 7));
                    break;
                case MidiType.TuneRequest:
                    rtpMidiEventHandler.OnMidiTuneRequest(deviceId);
                    break;
                case MidiType.Clock:
                    rtpMidiEventHandler.OnMidiTimingClock(deviceId);
                    break;
                case MidiType.Start:
                    rtpMidiEventHandler.OnMidiStart(deviceId);
                    break;
                case MidiType.Continue:
                    rtpMidiEventHandler.OnMidiContinue(deviceId);
                    break;
                case MidiType.Stop:
                    rtpMidiEventHandler.OnMidiStop(deviceId);
                    break;
                case MidiType.ActiveSensing:
                    rtpMidiEventHandler.OnMidiActiveSensing(deviceId);
                    break;
                case MidiType.SystemReset:
                    rtpMidiEventHandler.OnMidiReset(deviceId);
                    break;
            }
        }

        private void ClearIndefiniteArtifacts(RtpMidiParticipant participant)
        {
            RtpMidiIndefiniteState.Clear(rtpMidiEventHandler, GetDeviceId(participant));
        }
#endif

        private void RemoveParticipant(RtpMidiParticipant participant)
        {
#if ENABLE_RTP_MIDI_JOURNAL
            ClearIndefiniteArtifacts(participant);
#endif
            participants.Remove(participant);
            deviceConnectionListener.OnRtpMidiDeviceDetached(GetDeviceId(participant));
        }
    }
}