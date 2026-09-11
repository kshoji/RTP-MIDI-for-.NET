namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Recovery Journal section layout helpers (RFC 6295 §5, Appendix A.1).
    /// LENGTH bounds the section; chapter readers interpret the bodies.
    /// </summary>
    public static class RtpMidiJournalSection
    {
        public const int HeaderLength = 3;
        public const int SystemJournalHeaderLength = 2;
        public const int ChannelJournalHeaderLength = 3;

        public const byte FlagS = 0x80;
        public const byte FlagY = 0x40;
        public const byte FlagA = 0x20;
        public const byte FlagH = 0x10;

        public enum ConsumeResult
        {
            Ok,
            NotEnoughData,
            Invalid,
        }

        /// <summary>
        /// Encodes an empty recovery journal (Y=0, A=0, H=0, TOTCHAN=0) with S=1.
        /// Checkpoint Packet Seqnum is <paramref name="checkpointPacketSeqnum"/> (C = I yields an empty history).
        /// </summary>
        public static byte[] EncodeEmpty(ushort checkpointPacketSeqnum)
        {
            return new[]
            {
                FlagS,
                (byte)(checkpointPacketSeqnum >> 8),
                (byte)(checkpointPacketSeqnum & 0xff),
            };
        }

        /// <summary>
        /// Reads Checkpoint Packet Seqnum from a 3-octet recovery journal header.
        /// </summary>
        public static ushort GetCheckpointPacketSeqnum(byte[] header)
        {
            return (ushort)((header[1] << 8) | header[2]);
        }

        /// <summary>
        /// Consumes a recovery journal starting at <paramref name="offset"/> using LENGTH fields.
        /// Does not apply chapters. S=1 must not skip the body.
        /// </summary>
        public static ConsumeResult TryConsume(byte[] buffer, int offset, out int consumed)
        {
            consumed = 0;
            if (buffer == null || offset < 0 || offset > buffer.Length)
            {
                return ConsumeResult.Invalid;
            }

            var remaining = buffer.Length - offset;
            if (remaining < HeaderLength)
            {
                return ConsumeResult.NotEnoughData;
            }

            var flags = buffer[offset];
            var hasSystemJournal = (flags & FlagY) == FlagY;
            var hasChannelJournals = (flags & FlagA) == FlagA;
            var totalChannels = hasChannelJournals ? (flags & 0x0f) + 1 : 0;

            var total = HeaderLength;
            if (!hasSystemJournal && !hasChannelJournals)
            {
                consumed = HeaderLength;
                return ConsumeResult.Ok;
            }

            if (hasSystemJournal)
            {
                if (remaining < total + SystemJournalHeaderLength)
                {
                    return ConsumeResult.NotEnoughData;
                }

                var systemLength = ReadLength10(buffer[offset + total], buffer[offset + total + 1]);
                if (systemLength < SystemJournalHeaderLength)
                {
                    return ConsumeResult.Invalid;
                }

                total += systemLength;
                if (remaining < total)
                {
                    return ConsumeResult.NotEnoughData;
                }
            }

            if (hasChannelJournals)
            {
                for (var i = 0; i < totalChannels; i++)
                {
                    if (remaining < total + ChannelJournalHeaderLength)
                    {
                        return ConsumeResult.NotEnoughData;
                    }

                    var channelLength = ReadLength10(buffer[offset + total], buffer[offset + total + 1]);
                    if (channelLength < ChannelJournalHeaderLength)
                    {
                        return ConsumeResult.Invalid;
                    }

                    total += channelLength;
                    if (remaining < total)
                    {
                        return ConsumeResult.NotEnoughData;
                    }
                }
            }

            consumed = total;
            return ConsumeResult.Ok;
        }

        /// <summary>
        /// 10-bit LENGTH used by system and channel journal headers (includes that header).
        /// </summary>
        public static int ReadLength10(byte first, byte second)
        {
            return ((first & 0x03) << 8) | second;
        }
    }
}
