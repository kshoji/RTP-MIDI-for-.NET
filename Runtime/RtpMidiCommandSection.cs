using System;
using System.Collections.Generic;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// MIDI Command Section command boundaries and SysEx packet splits (RFC 6295 §3).
    /// Matches the send-side <c>Write</c> buffer rules so Chapter X logs the same segments as the wire.
    /// </summary>
    public static class RtpMidiCommandSection
    {
        public enum SysExKind
        {
            None,
            Complete,
            First,
            Middle,
            Last,
            Cancel,
            DroppedF7,
        }

        /// <summary>
        /// Appends one MIDI octet using the same SysEx split as <c>RtpMidiSession.Write</c>.
        /// When a segment is closed, <paramref name="flushed"/> is the MIDI section to send first.
        /// </summary>
        public static bool TryAppend(
            LinkedList<byte> buffer,
            byte datum,
            int maxBufferSize,
            out byte[] flushed)
        {
            flushed = null;
            if (buffer == null)
            {
                return false;
            }

            if (buffer.Count + 2 > maxBufferSize)
            {
                if (buffer.First?.Value != (byte)MidiType.SystemExclusive)
                {
                    return false;
                }

                buffer.AddLast((byte)MidiType.SystemExclusiveStart);
                flushed = ToArray(buffer);
                buffer.Clear();
                buffer.AddLast((byte)MidiType.SystemExclusiveEnd);
            }

            buffer.AddLast(datum);
            return true;
        }

        /// <summary>
        /// Splits a MIDI byte stream the way <c>Write</c> plus a final flush would.
        /// </summary>
        public static List<byte[]> Segment(IEnumerable<byte> midi, int maxBufferSize)
        {
            var packets = new List<byte[]>();
            var buffer = new LinkedList<byte>();
            if (midi == null)
            {
                return packets;
            }

            foreach (var datum in midi)
            {
                if (!TryAppend(buffer, datum, maxBufferSize, out var flushed))
                {
                    break;
                }

                if (flushed != null)
                {
                    packets.Add(flushed);
                }
            }

            if (buffer.Count > 0)
            {
                packets.Add(ToArray(buffer));
            }

            return packets;
        }

        /// <summary>
        /// Parses MIDI commands from a command section, skipping RTP-MIDI delta-time prefixes after the first command.
        /// </summary>
        public static List<byte[]> Parse(byte[] section)
        {
            var commands = new List<byte[]>();
            if (section == null || section.Length == 0)
            {
                return commands;
            }

            var i = 0;
            var first = true;
            while (i < section.Length)
            {
                if (!first)
                {
                    var stamp = TimestampOctets(section, i);
                    if (stamp <= 0)
                    {
                        break;
                    }

                    i += stamp;
                    if (i >= section.Length)
                    {
                        break;
                    }
                }

                first = false;
                var start = i;
                if (!TryAdvanceCommand(section, ref i) || i <= start)
                {
                    break;
                }

                var command = new byte[i - start];
                Buffer.BlockCopy(section, start, command, 0, command.Length);
                commands.Add(command);
            }

            return commands;
        }

        public static bool TryClassifySysEx(byte[] midi, out SysExKind kind, out byte[] data)
        {
            kind = SysExKind.None;
            data = Array.Empty<byte>();
            if (midi == null || midi.Length < 2)
            {
                return false;
            }

            var first = midi[0];
            var last = midi[midi.Length - 1];
            if (first == 0xf7 && midi.Length == 2 && last == 0xf4)
            {
                kind = SysExKind.Cancel;
                return true;
            }

            if (first == 0xf0 && last == 0xf7)
            {
                kind = SysExKind.Complete;
            }
            else if (first == 0xf0 && last == 0xf0)
            {
                kind = SysExKind.First;
            }
            else if (first == 0xf0 && last == 0xf5)
            {
                kind = SysExKind.DroppedF7;
            }
            else if (first == 0xf7 && last == 0xf0)
            {
                kind = SysExKind.Middle;
            }
            else if (first == 0xf7 && (last == 0xf7 || last == 0xf5))
            {
                kind = SysExKind.Last;
            }
            else
            {
                return false;
            }

            data = new byte[Math.Max(0, midi.Length - 2)];
            if (data.Length > 0)
            {
                Buffer.BlockCopy(midi, 1, data, 0, data.Length);
                for (var i = 0; i < data.Length; i++)
                {
                    data[i] &= 0x7f;
                }
            }

            return true;
        }

        public static byte[] WrapFinished(byte[] data)
        {
            data = data ?? Array.Empty<byte>();
            var payload = new byte[data.Length + 2];
            payload[0] = 0xf0;
            for (var i = 0; i < data.Length; i++)
            {
                payload[i + 1] = (byte)(data[i] & 0x7f);
            }

            payload[payload.Length - 1] = 0xf7;
            return payload;
        }

        private static byte[] ToArray(LinkedList<byte> buffer)
        {
            var bytes = new byte[buffer.Count];
            buffer.CopyTo(bytes, 0);
            return bytes;
        }

        private static int TimestampOctets(byte[] section, int index)
        {
            var n = 0;
            while (index < section.Length && n < 4)
            {
                var octet = section[index++];
                n++;
                if ((octet & 0x80) == 0)
                {
                    return n;
                }
            }

            return n == 0 ? -1 : n;
        }

        private static bool TryAdvanceCommand(byte[] section, ref int index)
        {
            if (index >= section.Length)
            {
                return false;
            }

            var status = section[index];
            if (status == 0xf0 || status == 0xf7)
            {
                index++;
                while (index < section.Length)
                {
                    var octet = section[index++];
                    if (octet == 0xf0 || octet == 0xf7 || octet == 0xf4 || octet == 0xf5)
                    {
                        return true;
                    }
                }

                return false;
            }

            int length;
            if (status >= 0xf8)
            {
                length = 1;
            }
            else if (status == 0xf1 || status == 0xf3)
            {
                length = 2;
            }
            else if (status == 0xf2)
            {
                length = 3;
            }
            else if (status == 0xf6 || status == 0xf4 || status == 0xf5)
            {
                length = 1;
            }
            else
            {
                var type = status & 0xf0;
                if (type == 0xc0 || type == 0xd0)
                {
                    length = 2;
                }
                else if (type >= 0x80 && type <= 0xe0)
                {
                    length = 3;
                }
                else
                {
                    return false;
                }
            }

            if (index + length > section.Length)
            {
                return false;
            }

            index += length;
            return true;
        }
    }
}
