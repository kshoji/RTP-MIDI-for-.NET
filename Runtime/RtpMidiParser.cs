using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Parser status
    /// </summary>
    public enum ParserResult
    {
        Processed,
        NotSureGiveMeMoreData,
        NotEnoughData,
        UnexpectedData,
        UnexpectedMidiData,
        UnexpectedJournalData,
        SessionNameVeryLong,
    }

    /// <summary>
    /// Parser for RTP MIDI
    /// </summary>
    public class RtpMidiParser
    {
        private bool rtpHeadersComplete;
        private short midiCommandLength;
        private byte rtpMidiFlags;

        private readonly RtpMidiSession session;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="session">the session</param>
        public RtpMidiParser(RtpMidiSession session)
        {
            this.session = session;
        }

        /// <summary>
        /// Parse received messages
        /// </summary>
        /// <param name="participant">the received participant</param>
        /// <param name="bufferData">the buffer data</param>
        /// <returns>the parser status</returns>
        public ParserResult Parse(RtpMidiParticipant participant, LinkedList<byte> bufferData)
        {
            var buffer = bufferData.ToArray();
            if (!rtpHeadersComplete)
            {
                var consumed = 12;
                if (buffer.Length < consumed)
                {
                    return ParserResult.NotSureGiveMeMoreData;
                }

                var rtp = new Rtp();
                rtp.vpxcc = buffer[0];
                rtp.mpayload = buffer[1];
                rtp.sequenceNr = (ushort)((buffer[2] << 8) | buffer[3]);
                rtp.timestamp = buffer[4] << 24 | buffer[5] << 16 | buffer[6] << 8 | buffer[7];
                rtp.ssrc = buffer[8] << 24 | buffer[9] << 16 | buffer[10] << 8 | buffer[11];

                var version = rtp.vpxcc >> 6;
                if (version != 2)
                {
                    return ParserResult.UnexpectedData;
                }

                var payloadType = rtp.mpayload & 0x7F;
                if (payloadType != 97)
                {
                    return ParserResult.UnexpectedData;
                }

                session.ReceivedRtp(rtp);

                consumed++;
                if (buffer.Length < consumed)
                {
                    return ParserResult.NotSureGiveMeMoreData;
                }

                // RTP-MIDI starts with 4 bits of flags...
                rtpMidiFlags = buffer[12];

                // ...followed by a length-field of at least 4 bits
                midiCommandLength = (short)(rtpMidiFlags & 0x0f);

                // see if we have small or large len-field
                if ((rtpMidiFlags & 0x80) == 0x80)
                {
                    consumed++;
                    if (buffer.Length < consumed)
                    {
                        return ParserResult.NotSureGiveMeMoreData;
                    }

                    // long header
                    midiCommandLength = (short)(midiCommandLength << 8 | buffer[13]);
                }

                // consume all the bytes that made up this message
                for (var i = 0; i < consumed; i++)
                {
                    bufferData.RemoveFirst();
                }

                rtpHeadersComplete = true;
            }

            // Always a MIDI section
            if (midiCommandLength > 0)
            {
#if ENABLE_RTP_MIDI_JOURNAL
                if (!participant.playMidiCommands)
                {
                    var skip = SkipMidiSection(bufferData, midiCommandLength);
                    if (skip != ParserResult.Processed)
                    {
                        return skip;
                    }
                }
                else
#endif
                {
                    var retVal = DecodeMidiSection(participant, bufferData);
                    switch (retVal)
                    {
                        case ParserResult.Processed:
                            break;
                        case ParserResult.UnexpectedMidiData:
                            // already processed MIDI data will be played
                            rtpHeadersComplete = false;
                            break;
                        default:
                            return retVal;
                    }
                }
            }

            if ((rtpMidiFlags & 0x40) == 0x40)
            {
#if ENABLE_RTP_MIDI_JOURNAL
                var retVal = DecodeJournalSection(participant, bufferData, participant.applyRecoveryJournal);
#else
                var retVal = DecodeJournalSection(bufferData);
#endif
                switch (retVal)
                {
                    case ParserResult.Processed:
                        break;
                    case ParserResult.UnexpectedJournalData:
                        rtpHeadersComplete = false;
                        break;
                    default:
                        return retVal;
                }
            }

            rtpHeadersComplete = false;

            return ParserResult.Processed;
        }

        private ParserResult DecodeMidiSection(RtpMidiParticipant participant, LinkedList<byte> bufferData)
        {
            var buffer = bufferData.ToArray();

            int cmdCount = 0;
            byte runningStatus = 0;

            // Multiple MIDI-commands might follow - the exact number can only be discovered by really decoding the commands!
            while (midiCommandLength > 0)
            {
                // for the first command we only have a delta-time if Z-Flag is set
                if (cmdCount != 0 || (rtpMidiFlags & 0x20) == 0x20)
                {
                    var consumed = DecodeTime(buffer);
                    if (consumed < 0)
                    {
                        return ParserResult.NotEnoughData;
                    }
                    
                    midiCommandLength -= (byte)consumed;

                    for (var i = 0; i < consumed; i++)
                    {
                        bufferData.RemoveFirst();
                    }
                    buffer = bufferData.ToArray();

                    if (midiCommandLength > 0 && 0 >= buffer.Length)
                    {
                        return ParserResult.NotEnoughData;
                    }
                }

                if (midiCommandLength > 0)
                {
                    var consumed = DecodeMidi(participant, bufferData, ref runningStatus);
                    buffer = bufferData.ToArray();
                    if (consumed == 0 || consumed > buffer.Length)
                    {
                        return ParserResult.NotEnoughData;
                    }

                    if (consumed > midiCommandLength)
                    {
                        return ParserResult.UnexpectedMidiData;
                    }

                    midiCommandLength -= consumed;

                    for (var i = 0; i < consumed; i++)
                    {
                        bufferData.RemoveFirst();
                    }
                    buffer = bufferData.ToArray();

                    if (midiCommandLength > 0 && 0 >= buffer.Length)
                    {
                        return ParserResult.NotEnoughData;
                    }

                    cmdCount++;
                }
            }

            return ParserResult.Processed;
        }

        private int DecodeTime(byte[] buffer)
        {
            byte consumed = 0;
            int deltatime = 0;

            /* RTP-MIDI deltatime is "compressed" using only the necessary amount of octets */
            for (var j = 0; j < 4; j++)
            {
                if (buffer.Length < consumed)
                {
                    return -1;
                }

                byte octet = buffer[consumed];
                deltatime = (deltatime << 7) | (octet & 0x7f);
                consumed++;

                if ((octet & 0x80) == 0)
                {
                    break;
                }
            }
            return consumed;
        }

        private byte DecodeMidi(RtpMidiParticipant participant, LinkedList<byte> bufferData, ref byte runningStatus)
        {
            byte consumed = 0;
            var buffer = bufferData.ToArray();
            if (buffer.Length < 1)
            {
                return 0;
            }

            var octet = buffer[0];

            /* MIDI realtime-data . one octet  -- unlike serial-wired MIDI realtime-commands in RTP-MIDI will
             * not be intermingled with other MIDI-commands, so we handle this case right here and return */
            if (octet >= 0xf8)
            {
                session.ReceivedMidi(octet);
                session.ReceivedMidi(participant, (MidiType)octet, new []{octet});

                return 1;
            }

            /* see if this first octet is a status message */
            if ((octet & 0x80) == 0)
            {
                /* if we have no running status yet. error */
                if ((runningStatus & 0x80) == 0)
                {
                    return 0;
                }

                /* our first octet is "virtual" coming from a preceding MIDI-command,
                 * so actually we have not really consumed anything yet */
                octet = runningStatus;
            }
            else
            {
                /* Let's see how this octet influences our running-status */
                /* if we have a "normal" MIDI-command then the new status replaces the current running-status */
                if (octet < 0xf0)
                {
                    runningStatus = octet;
                }
                else
                {
                    /* system-realtime-commands maintain the current running-status
                     * other system-commands clear the running-status, since we
                     * already handled realtime, we can reset it here */
                    runningStatus = 0;
                }
                consumed++;
            }

            /* non-system MIDI-commands encode the command in the high nibble and the channel
             * in the low nibble - so we will take care of those cases next */
            if (octet < 0xf0)
            {
                var type = (MidiType)(octet & 0xf0);
                switch (type)
                {
                    case MidiType.NoteOff:
                        consumed += 2;
                        break;
                    case MidiType.NoteOn:
                        consumed += 2;
                        break;
                    case MidiType.AfterTouchPoly:
                        consumed += 2;
                        break;
                    case MidiType.ControlChange:
                        consumed += 2;
                        break;
                    case MidiType.ProgramChange:
                        consumed += 1;
                        break;
                    case MidiType.AfterTouchChannel:
                        consumed += 1;
                        break;
                    case MidiType.PitchBend:
                        consumed += 2;
                        break;
                }

                if (buffer.Length < consumed)
                {
                    return 0;
                }

                bool addRunningStatus = runningStatus != 0 && (buffer[0] & 0x80) == 0;
                byte[] midiData = new byte[consumed + (addRunningStatus ? 1 : 0)];
                if (addRunningStatus)
                {
                    midiData[0] = runningStatus;
                }
                for (var j = 0; j < consumed; j++)
                {
                    session.ReceivedMidi(buffer[j]);
                    midiData[j + (addRunningStatus ? 1 : 0)] = buffer[j];
                }
                session.ReceivedMidi(participant, type, midiData);
                
                return consumed;
            }

            /* Here we catch the remaining system-common commands */
            switch ((MidiType)octet)
            {
                case MidiType.SystemExclusiveStart:
                case MidiType.SystemExclusiveEnd:
                    consumed = DecodeMidiSysEx(bufferData);
                    buffer = bufferData.ToArray();
                    if (consumed > RtpMidiParticipant.MaxBufferSize)
                    {
                        return consumed;
                    }
                    break;
                case MidiType.TimeCodeQuarterFrame:
                    consumed += 1;
                    break;
                case MidiType.SongPosition:
                    consumed += 2;
                    break;
                case MidiType.SongSelect:
                    consumed += 1;
                    break;
                case MidiType.TuneRequest:
                    break;
            }

            {
                bool addRunningStatus = runningStatus != 0 && (buffer[0] & 0x80) != 0;
                byte[] midiData = new byte[consumed + (addRunningStatus ? 1 : 0)];
                if (runningStatus != 0)
                {
                    midiData[0] = runningStatus;
                }
                for (var j = 0; j < consumed; j++)
                {
                    session.ReceivedMidi(buffer[j]);
                    midiData[j + (addRunningStatus ? 1 : 0)] = buffer[j];
                }
                session.ReceivedMidi(participant, (MidiType)octet, midiData);
            }

            return consumed;
        }

        private byte DecodeMidiSysEx(LinkedList<byte> bufferData)
        {
            byte consumed = 1; // beginning SysEx Token is not counted (as it could remain)
            var buffer = bufferData.ToArray();

            while (consumed < buffer.Length)
            {
                var octet = buffer[consumed++];
                if (octet == (int)MidiType.SystemExclusiveEnd) // Complete message
                {
                    return consumed;
                }
                if (octet == (int)MidiType.SystemExclusiveStart) // Start
                {
                    return consumed;
                }
            }
    
            // begin of the SysEx is found, not the end.
            // so transmit what we have, add a stop-token at the end,
            // remove the byes, modify the length and indicate
            // not-enough data, so we buffer gets filled with the remaining bytes.
    
            // to compensate for adding the sysex at the end.
            consumed--;

            // send MIDI data
            for (var i = 0; i < consumed; i++)
            {
                session.ReceivedMidi(buffer[i]);
            }
            session.ReceivedMidi((byte)MidiType.SystemExclusiveStart);

            // Remove the bytes that were submitted
            for (var i = 0; i < consumed; i++)
            {
                bufferData.RemoveFirst();
            }
            bufferData.AddFirst((byte)MidiType.SystemExclusiveEnd);

            midiCommandLength -= consumed;
            midiCommandLength += 1; // adding the manual SysEx SystemExclusiveEnd

            // indicates split SysEx
            return RtpMidiParticipant.MaxBufferSize + 1;
        }

#if ENABLE_RTP_MIDI_JOURNAL
        private ParserResult SkipMidiSection(LinkedList<byte> bufferData, short length)
        {
            if (bufferData.Count < length)
            {
                return ParserResult.NotEnoughData;
            }

            for (var i = 0; i < length; i++)
            {
                bufferData.RemoveFirst();
            }

            midiCommandLength = 0;
            return ParserResult.Processed;
        }

        private ParserResult DecodeJournalSection(RtpMidiParticipant participant, LinkedList<byte> bufferData, bool apply)
        {
            var buffer = bufferData.ToArray();
            var result = RtpMidiJournalSection.TryConsume(buffer, 0, out var consumed);
            switch (result)
            {
                case RtpMidiJournalSection.ConsumeResult.NotEnoughData:
                    return ParserResult.NotEnoughData;
                case RtpMidiJournalSection.ConsumeResult.Invalid:
                    rtpHeadersComplete = false;
                    return ParserResult.UnexpectedJournalData;
            }

            if (apply)
            {
                session.ApplyRecoveryJournal(participant, buffer, consumed);
            }

            for (var i = 0; i < consumed; i++)
            {
                bufferData.RemoveFirst();
            }

            return ParserResult.Processed;
        }
#else
        private ParserResult DecodeJournalSection(LinkedList<byte> bufferData)
        {
            var buffer = bufferData.ToArray();
            var result = RtpMidiJournalSection.TryConsume(buffer, 0, out var consumed);
            switch (result)
            {
                case RtpMidiJournalSection.ConsumeResult.NotEnoughData:
                    return ParserResult.NotEnoughData;
                case RtpMidiJournalSection.ConsumeResult.Invalid:
                    rtpHeadersComplete = false;
                    return ParserResult.UnexpectedJournalData;
            }

            for (var i = 0; i < consumed; i++)
            {
                bufferData.RemoveFirst();
            }

            return ParserResult.Processed;
        }
#endif
    }

    /// <summary>
    /// Parser for Apple MIDI
    /// </summary>
    public class AppleMidiParser
    {
        private readonly RtpMidiSession session;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="session"></param>
        public AppleMidiParser(RtpMidiSession session)
        {
            this.session = session;
        }

        /// <summary>
        /// Parser for RTP MIDI
        /// </summary>
        public ParserResult Parse(LinkedList<byte> bufferData, PortType portType)
        {
            var buffer = bufferData.ToArray();
            if (buffer.Length < 4)
            {
                return ParserResult.NotSureGiveMeMoreData;
            }

            if (buffer[0] != RtpMidiConstants.Signature[0] || buffer[1] != RtpMidiConstants.Signature[1])
            {
                return ParserResult.UnexpectedData;
            }

            byte[] command = {buffer[2], buffer[3]};
            if (command.SequenceEqual(RtpMidiConstants.Invitation))
            {
                if (buffer.Length < 16)
                {
                    return ParserResult.NotEnoughData;
                }

                byte[] protocolVersion = { buffer[4], buffer[5], buffer[6], buffer[7] };
                if (!protocolVersion.SequenceEqual(RtpMidiConstants.ProtocolVersion))
                {
                    return ParserResult.UnexpectedData;
                }
                
                // parse session name(remain bytes)
                var sessionName = new byte[buffer.Length - 16];
                Array.Copy(buffer, 16, sessionName, 0, buffer.Length - 16);

                // parse invitation(8 bytes)
                var invitation = new RtpMidiInvitation(
                    buffer[8] << 24 | buffer[9] << 16 | buffer[10] << 8 | buffer[11],
                    buffer[12] << 24 | buffer[13] << 16 | buffer[14] << 8 | buffer[15],
                    Encoding.Default.GetString(sessionName)
                );

                // consume all the bytes that made up this message
                for (var i = 0; i < 16; i++)
                {
                    bufferData.RemoveFirst();
                }

                session.ReceivedInvitation(invitation, portType);

                return ParserResult.Processed;
            }
            if (command.SequenceEqual(RtpMidiConstants.EndSession))
            {
                if (buffer.Length < 16)
                {
                    return ParserResult.NotEnoughData;
                }

                byte[] protocolVersion = { buffer[4], buffer[5], buffer[6], buffer[7] };
                if (!protocolVersion.SequenceEqual(RtpMidiConstants.ProtocolVersion))
                {
                    return ParserResult.UnexpectedData;
                }

                var endSession = new RtpMidiEndSession(
                    buffer[8] << 24 | buffer[9] << 16 | buffer[10] << 8 | buffer[11],
                    buffer[12] << 24 | buffer[13] << 16 | buffer[14] << 8 | buffer[15]
                );

                // consume all the bytes that made up this message
                for (var i = 0; i < 16; i++)
                {
                    bufferData.RemoveFirst();
                }

                session.ReceivedEndSession(endSession);

                return ParserResult.Processed;
            }
            if (command.SequenceEqual(RtpMidiConstants.Synchronization))
            {
                if (buffer.Length < 36)
                {
                    return ParserResult.NotEnoughData;
                }

                var synchronization = new RtpMidiSynchronization(
                    buffer[4] << 24 | buffer[5] << 16 | buffer[6] << 8 | buffer[7],
                    buffer[8], new[]
                    {
                        ((long)buffer[12] << 56) | ((long)buffer[13] << 48) | ((long)buffer[14] << 40) | ((long)buffer[15] << 32) |
                        ((long)buffer[16] << 24) | ((long)buffer[17] << 16) | ((long)buffer[18] << 8) | buffer[19],
                        ((long)buffer[20] << 56) | ((long)buffer[21] << 48) | ((long)buffer[22] << 40) | ((long)buffer[23] << 32) |
                        ((long)buffer[24] << 24) | ((long)buffer[25] << 16) | ((long)buffer[26] << 8) | buffer[27],
                        ((long)buffer[28] << 56) | ((long)buffer[29] << 48) | ((long)buffer[30] << 40) | ((long)buffer[31] << 32) |
                        ((long)buffer[32] << 24) | ((long)buffer[33] << 16) | ((long)buffer[34] << 8) | buffer[35],
                    }
                );

                // consume all the bytes that made up this message
                for (var i = 0; i < 36; i++)
                {
                    bufferData.RemoveFirst();
                }

                session.ReceivedSynchronization(synchronization);

                return ParserResult.Processed;
            }
            if (command.SequenceEqual(RtpMidiConstants.ReceiverFeedback))
            {
                if (buffer.Length < 12)
                {
                    return ParserResult.NotEnoughData;
                }

                var receiverFeedback = new RtpMidiReceiverFeedback(
                    buffer[4] << 24 | buffer[5] << 16 | buffer[6] << 8 | buffer[7],
                    (ushort)((buffer[8] << 8) | buffer[9])
                );

                // consume all the bytes that made up this message
                for (var i = 0; i < 12; i++)
                {
                    bufferData.RemoveFirst();
                }

                session.ReceivedReceiverFeedback(receiverFeedback);

                return ParserResult.Processed;
            }
            if (command.SequenceEqual(RtpMidiConstants.InvitationAccepted))
            {
                if (buffer.Length < 16)
                {
                    return ParserResult.NotEnoughData;
                }
                byte[] protocolVersion = { buffer[4], buffer[5], buffer[6], buffer[7] };
                if (!protocolVersion.SequenceEqual(RtpMidiConstants.ProtocolVersion))
                {
                    return ParserResult.UnexpectedData;
                }

                // parse session name(remain bytes)
                var sessionName = new byte[buffer.Length - 16];
                Array.Copy(buffer, 16, sessionName, 0, buffer.Length - 16);

                // parse invitation(8 bytes)
                var invitationAccepted = new RtpMidiInvitationAccepted(
                    buffer[8] << 24 | buffer[9] << 16 | buffer[10] << 8 | buffer[11],
                    buffer[12] << 24 | buffer[13] << 16 | buffer[14] << 8 | buffer[15],
                    Encoding.Default.GetString(sessionName)
                );

                // consume all the bytes that made up this message
                bufferData.Clear();

                session.ReceivedInvitationAccepted(invitationAccepted, portType);

                return ParserResult.Processed;
            }
            if (command.SequenceEqual(RtpMidiConstants.InvitationRejected))
            {
                if (buffer.Length < 16)
                {
                    return ParserResult.NotEnoughData;
                }
                byte[] protocolVersion = { buffer[4], buffer[5], buffer[6], buffer[7] };
                if (!protocolVersion.SequenceEqual(RtpMidiConstants.ProtocolVersion))
                {
                    return ParserResult.UnexpectedData;
                }

                // parse session name(remain bytes)
                var sessionName = new byte[buffer.Length - 16];
                Array.Copy(buffer, 16, sessionName, 0, buffer.Length - 16);

                // parse invitation(8 bytes)
                var invitationRejected = new RtpMidiInvitationRejected(
                    buffer[8] << 24 | buffer[9] << 16 | buffer[10] << 8 | buffer[11],
                    buffer[12] << 24 | buffer[13] << 16 | buffer[14] << 8 | buffer[15],
                    Encoding.Default.GetString(sessionName)
                );

                // consume all the bytes that made up this message
                bufferData.Clear();

                session.ReceivedInvitationRejected(invitationRejected);

                return ParserResult.Processed;
            }
            if (command.SequenceEqual(RtpMidiConstants.BitrateReceiveLimit))
            {
                if (buffer.Length < 12)
                {
                    return ParserResult.NotEnoughData;
                }

                var bitrateReceiveLimit = new RtpMidiBitrateReceiveLimit(
                    buffer[4] << 24 | buffer[5] << 16 | buffer[6] << 8 | buffer[7],
                    buffer[8] << 24 | buffer[9] << 16 | buffer[10] << 8 | buffer[11]
                );

                // consume all the bytes that made up this message
                for (var i = 0; i < 12; i++)
                {
                    bufferData.RemoveFirst();
                }

                session.ReceivedBitrateReceiveLimit(bitrateReceiveLimit);

                return ParserResult.Processed;
            }

            return ParserResult.UnexpectedData;
        }
    }
}