using System;
using System.Collections.Generic;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Chapter C tool carried on a recovered Control Change so the receiver can apply a delta.
    /// </summary>
    public enum RecoveredControlTool
    {
        None,
        Value,
        Toggle,
        Count,
    }

    /// <summary>
    /// MIDI command recovered from a recovery journal chapter.
    /// </summary>
    public readonly struct RecoveredMidi
    {
        public RecoveredMidi(
            MidiType type,
            int channel,
            int data1,
            int data2,
            byte[] payload = null,
            RecoveredControlTool controlTool = RecoveredControlTool.None,
            int controlAlt = 0,
            int noteRefCount = -1)
        {
            Type = type;
            Channel = channel;
            Data1 = data1;
            Data2 = data2;
            Payload = payload;
            ControlTool = controlTool;
            ControlAlt = controlAlt;
            NoteRefCount = noteRefCount;
        }

        public MidiType Type { get; }
        public int Channel { get; }
        public int Data1 { get; }
        public int Data2 { get; }
        public byte[] Payload { get; }
        public RecoveredControlTool ControlTool { get; }
        public int ControlAlt { get; }
        public int NoteRefCount { get; }
    }

    /// <summary>
    /// Channel recovery journal chapters (RFC 6295 Appendix A).
    /// Encode is read-only with respect to recorded history and note reference counts.
    /// </summary>
    public static class RtpMidiNoteJournal
    {
        public const byte TocP = 0x80;
        public const byte TocC = 0x40;
        public const byte TocM = 0x20;
        public const byte TocW = 0x10;
        public const byte TocN = 0x08;
        public const byte TocE = 0x04;
        public const byte TocT = 0x02;
        public const byte TocA = 0x01;

        public readonly struct HistoryItem
        {
            public HistoryItem(ushort packetSequence, byte[] midi)
                : this(packetSequence, midi, default)
            {
            }

            public HistoryItem(ushort packetSequence, byte[] midi, RtpMidiControlJournal.ControlMeta meta)
                : this(packetSequence, midi, meta, 0)
            {
            }

            public HistoryItem(ushort packetSequence, byte[] midi, RtpMidiControlJournal.ControlMeta meta, int sysExCountAfter)
            {
                PacketSequence = packetSequence;
                Midi = midi;
                Meta = meta;
                SysExCountAfter = sysExCountAfter;
            }

            public ushort PacketSequence { get; }
            public byte[] Midi { get; }
            public RtpMidiControlJournal.ControlMeta Meta { get; }
            public int SysExCountAfter { get; }
        }

        /// <summary>
        /// Updates session-lifetime note reference counts. Called when a command is committed, not when encoded.
        /// </summary>
        public static void ApplyCommitted(int[,] noteRefCount, byte[] midi)
        {
            if (noteRefCount == null || midi == null || midi.Length == 0)
            {
                return;
            }

            if (IsResetState(midi))
            {
                ClearAll(noteRefCount);
                return;
            }

            if (IsNotesResetController(midi, out var channel))
            {
                ClearChannel(noteRefCount, channel);
                return;
            }

            if (TryGetNote(midi, out channel, out var note, out var isNoteOn, out _))
            {
                if (isNoteOn)
                {
                    noteRefCount[channel, note]++;
                }
                else if (noteRefCount[channel, note] > 0)
                {
                    noteRefCount[channel, note]--;
                }
            }
        }

        public static byte[] Encode(
            IReadOnlyList<HistoryItem> history,
            int[,] noteRefCount,
            ushort packetSequenceI,
            ushort checkpointC)
        {
            return Encode(history, noteRefCount, null, null, packetSequenceI, checkpointC);
        }

        /// <summary>
        /// Encodes a recovery journal whose checkpoint history is [C, I).
        /// Channel chapters are P, C, M, W, N, E, T, and A. System chapters are D, V, Q, F, and X.
        /// </summary>
        public static byte[] Encode(
            IReadOnlyList<HistoryItem> history,
            int[,] noteRefCount,
            RtpMidiControlState controlState,
            ushort packetSequenceI,
            ushort checkpointC)
        {
            return Encode(history, noteRefCount, controlState, null, packetSequenceI, checkpointC);
        }

        public static byte[] Encode(
            IReadOnlyList<HistoryItem> history,
            int[,] noteRefCount,
            RtpMidiControlState controlState,
            RtpMidiSystemState systemState,
            ushort packetSequenceI,
            ushort checkpointC)
        {
            if (history == null || checkpointC == packetSequenceI)
            {
                return RtpMidiJournalSection.EncodeEmpty(checkpointC);
            }

            var hasSystem = RtpMidiSystemJournal.TryEncode(
                history,
                systemState,
                packetSequenceI,
                checkpointC,
                out var system,
                out var systemS);
            var channels = BuildChannels(history, noteRefCount, controlState, packetSequenceI, checkpointC);
            if (!hasSystem && channels.Count == 0)
            {
                return RtpMidiJournalSection.EncodeEmpty(checkpointC);
            }

            var payload = new List<byte>();
            var topS = true;
            if (hasSystem)
            {
                if (!systemS)
                {
                    topS = false;
                }

                payload.AddRange(system);
            }

            foreach (var channel in channels)
            {
                if (!channel.S)
                {
                    topS = false;
                }

                payload.AddRange(channel.Bytes);
            }

            var flags = topS ? RtpMidiJournalSection.FlagS : 0;
            if (hasSystem)
            {
                flags |= RtpMidiJournalSection.FlagY;
            }

            if (channels.Count > 0)
            {
                flags |= RtpMidiJournalSection.FlagA | ((channels.Count - 1) & 0x0f);
            }

            var header = new byte[3 + payload.Count];
            header[0] = (byte)flags;
            header[1] = (byte)(checkpointC >> 8);
            header[2] = (byte)(checkpointC & 0xff);
            payload.CopyTo(header, 3);
            return header;
        }

        /// <summary>
        /// Decodes Chapter N / E and returns renderer commands. Malformed journals yield false and no commands.
        /// </summary>
        public static bool TryDecode(byte[] journal, int length, out IReadOnlyList<RecoveredMidi> commands)
        {
            var list = new List<RecoveredMidi>();
            commands = list;
            if (journal == null || length < RtpMidiJournalSection.HeaderLength || journal.Length < length)
            {
                return false;
            }

            var flags = journal[0];
            var offset = RtpMidiJournalSection.HeaderLength;
            if ((flags & RtpMidiJournalSection.FlagY) == RtpMidiJournalSection.FlagY)
            {
                if (length < offset + RtpMidiJournalSection.SystemJournalHeaderLength)
                {
                    return false;
                }

                if (!RtpMidiSystemJournal.TryRead(journal, ref offset, length, list))
                {
                    return false;
                }
            }

            if ((flags & RtpMidiJournalSection.FlagA) != RtpMidiJournalSection.FlagA)
            {
                return true;
            }

            var totalChannels = (flags & 0x0f) + 1;
            for (var i = 0; i < totalChannels; i++)
            {
                if (length < offset + RtpMidiJournalSection.ChannelJournalHeaderLength)
                {
                    return false;
                }

                var channelLength = RtpMidiJournalSection.ReadLength10(journal[offset], journal[offset + 1]);
                if (channelLength < RtpMidiJournalSection.ChannelJournalHeaderLength || offset + channelLength > length)
                {
                    return false;
                }

                var channel = (journal[offset] >> 3) & 0x0f;
                var toc = journal[offset + 2];
                var body = offset + RtpMidiJournalSection.ChannelJournalHeaderLength;
                var bodyEnd = offset + channelLength;

                var cursor = body;
                if ((toc & TocP) == TocP &&
                    !RtpMidiVoiceJournal.TryReadChapterP(journal, ref cursor, bodyEnd, channel, list))
                {
                    return false;
                }

                if ((toc & TocC) == TocC &&
                    !RtpMidiControlJournal.TryReadChapterC(journal, ref cursor, bodyEnd, channel, list))
                {
                    return false;
                }

                if ((toc & TocM) == TocM &&
                    !RtpMidiControlJournal.TryReadChapterM(journal, ref cursor, bodyEnd, channel, list))
                {
                    return false;
                }

                if ((toc & TocW) == TocW &&
                    !RtpMidiVoiceJournal.TryReadChapterW(journal, ref cursor, bodyEnd, channel, list))
                {
                    return false;
                }

                if (!TryApplyChannelNotes(journal, ref cursor, bodyEnd, toc, channel, list))
                {
                    return false;
                }

                if ((toc & TocT) == TocT &&
                    !RtpMidiVoiceJournal.TryReadChapterT(journal, ref cursor, bodyEnd, channel, list))
                {
                    return false;
                }

                if ((toc & TocA) == TocA &&
                    !RtpMidiVoiceJournal.TryReadChapterA(journal, ref cursor, bodyEnd, channel, list))
                {
                    return false;
                }

                if (cursor != bodyEnd)
                {
                    return false;
                }

                offset += channelLength;
            }

            return offset == length || offset < length;
        }

        private static List<ChannelJournal> BuildChannels(
            IReadOnlyList<HistoryItem> history,
            int[,] noteRefCount,
            RtpMidiControlState controlState,
            ushort packetSequenceI,
            ushort checkpointC)
        {
            var previousPacket = (ushort)(packetSequenceI - 1);
            var latest = new LatestNote[16, 128];
            var noteOffInPrevious = new bool[16];
            var order = 0;

            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                var midi = item.Midi;
                if (midi == null || midi.Length == 0)
                {
                    continue;
                }

                if (IsResetState(midi))
                {
                    ClearLatest(latest);
                    continue;
                }

                if (IsNotesResetController(midi, out var resetChannel))
                {
                    ClearLatestChannel(latest, resetChannel);
                    continue;
                }

                if (!RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI))
                {
                    continue;
                }

                if (!TryGetNote(midi, out var channel, out var note, out var isNoteOn, out var velocity))
                {
                    continue;
                }

                if (!isNoteOn && item.PacketSequence == previousPacket)
                {
                    noteOffInPrevious[channel] = true;
                }

                latest[channel, note] = new LatestNote
                {
                    Present = true,
                    IsNoteOn = isNoteOn,
                    Velocity = velocity,
                    PacketSequence = item.PacketSequence,
                    Order = order++,
                };
            }

            var channels = new List<ChannelJournal>();
            for (var channel = 0; channel < 16; channel++)
            {
                var ons = new List<LatestNote>();
                var offs = new List<LatestNote>();
                for (var note = 0; note < 128; note++)
                {
                    if (!latest[channel, note].Present)
                    {
                        continue;
                    }

                    var entry = latest[channel, note];
                    entry.Note = note;
                    if (entry.IsNoteOn)
                    {
                        ons.Add(entry);
                    }
                    else
                    {
                        offs.Add(entry);
                    }
                }

                if (ons.Count == 0 && offs.Count == 0)
                {
                    ons.Clear();
                }

                ons.Sort((a, b) => a.Order.CompareTo(b.Order));
                var hasNotes = ons.Count > 0 || offs.Count > 0;
                byte[] chapterN = Array.Empty<byte>();
                byte[] chapterE = Array.Empty<byte>();
                var chapterNS = true;
                var chapterES = true;
                var hasE = false;
                if (hasNotes)
                {
                    chapterN = EncodeChapterN(ons, offs, noteOffInPrevious[channel], previousPacket, out chapterNS);
                    chapterE = EncodeChapterE(ons, offs, noteRefCount, channel, previousPacket, out chapterES, out hasE);
                }

                RtpMidiVoiceJournal.EncodeChannel(
                    history,
                    channel,
                    packetSequenceI,
                    checkpointC,
                    out var voiceS,
                    out var voiceToc,
                    out var chapterP,
                    out var chapterW,
                    out var chapterTail,
                    out var banks);
                RtpMidiControlJournal.EncodeChannel(
                    history,
                    controlState,
                    channel,
                    packetSequenceI,
                    checkpointC,
                    banks,
                    out var controlS,
                    out var controlToc,
                    out var controlBody);
                if (!hasNotes && controlToc == 0 && voiceToc == 0)
                {
                    continue;
                }

                var channelS = voiceS && controlS && chapterNS && chapterES;
                var toc = (byte)(voiceToc | controlToc | (hasNotes ? TocN : 0) | (hasE ? TocE : 0));
                var body = new List<byte>();
                if (chapterP.Length > 0)
                {
                    body.AddRange(chapterP);
                }

                if (controlBody.Length > 0)
                {
                    body.AddRange(controlBody);
                }

                if (chapterW.Length > 0)
                {
                    body.AddRange(chapterW);
                }

                if (hasNotes)
                {
                    body.AddRange(chapterN);
                    if (hasE)
                    {
                        body.AddRange(chapterE);
                    }
                }

                if (chapterTail.Length > 0)
                {
                    body.AddRange(chapterTail);
                }

                channels.Add(new ChannelJournal
                {
                    S = channelS,
                    Bytes = EncodeChannel(channel, channelS, toc, body),
                });
            }

            return channels;
        }

        private static byte[] EncodeChapterN(
            List<LatestNote> ons,
            List<LatestNote> offs,
            bool noteOffInPrevious,
            ushort previousPacket,
            out bool s)
        {
            s = true;
            var logCount = ons.Count;
            var encodedLen = logCount == 128 ? 127 : logCount;
            int low;
            int high;
            int offOctets;
            if (offs.Count == 0)
            {
                low = 15;
                high = logCount == 127 ? 1 : 0;
                offOctets = 0;
            }
            else
            {
                var min = offs[0].Note;
                var max = offs[0].Note;
                for (var i = 1; i < offs.Count; i++)
                {
                    if (offs[i].Note < min)
                    {
                        min = offs[i].Note;
                    }

                    if (offs[i].Note > max)
                    {
                        max = offs[i].Note;
                    }
                }

                low = min / 8;
                high = max / 8;
                offOctets = high - low + 1;
            }

            var bytes = new byte[2 + (logCount * 2) + offOctets];
            bytes[0] = (byte)((noteOffInPrevious ? 0 : 0x80) | (encodedLen & 0x7f));
            bytes[1] = (byte)((low << 4) | (high & 0x0f));
            if (noteOffInPrevious)
            {
                s = false;
            }

            var offset = 2;
            for (var i = 0; i < ons.Count; i++)
            {
                var on = ons[i];
                var logS = on.PacketSequence != previousPacket;
                if (!logS)
                {
                    s = false;
                }

                bytes[offset++] = (byte)((logS ? 0x80 : 0) | (on.Note & 0x7f));
                bytes[offset++] = (byte)(0x80 | (on.Velocity & 0x7f));
            }

            for (var octet = 0; octet < offOctets; octet++)
            {
                byte bits = 0;
                var baseNote = (low + octet) * 8;
                for (var bit = 0; bit < 8; bit++)
                {
                    var note = baseNote + bit;
                    if (ContainsOff(offs, note))
                    {
                        bits |= (byte)(0x80 >> bit);
                    }
                }

                bytes[offset++] = bits;
            }

            return bytes;
        }

        private static byte[] EncodeChapterE(
            List<LatestNote> ons,
            List<LatestNote> offs,
            int[,] noteRefCount,
            int channel,
            ushort previousPacket,
            out bool s,
            out bool hasChapter)
        {
            s = true;
            hasChapter = false;
            var logs = new List<ELog>();
            CollectExtraLogs(ons, noteRefCount, channel, previousPacket, true, logs);
            CollectExtraLogs(offs, noteRefCount, channel, previousPacket, false, logs);
            if (logs.Count == 0)
            {
                return Array.Empty<byte>();
            }

            if (logs.Count > 128)
            {
                for (var i = logs.Count - 1; i >= 0 && logs.Count > 128; i--)
                {
                    if (logs[i].VelocityLog)
                    {
                        logs.RemoveAt(i);
                    }
                }
            }

            logs.Sort((a, b) =>
            {
                var order = a.Order.CompareTo(b.Order);
                if (order != 0)
                {
                    return order;
                }

                return a.VelocityLog.CompareTo(b.VelocityLog);
            });

            var bytes = new byte[1 + (logs.Count * 2)];
            var headerS = true;
            for (var i = 0; i < logs.Count; i++)
            {
                if (!logs[i].S)
                {
                    headerS = false;
                }
            }

            s = headerS;
            bytes[0] = (byte)((headerS ? 0x80 : 0) | ((logs.Count - 1) & 0x7f));
            var offset = 1;
            for (var i = 0; i < logs.Count; i++)
            {
                var log = logs[i];
                bytes[offset++] = (byte)((log.S ? 0x80 : 0) | (log.Note & 0x7f));
                bytes[offset++] = (byte)((log.VelocityLog ? 0x80 : 0) | (log.Value & 0x7f));
            }

            hasChapter = true;
            return bytes;
        }

        private static void CollectExtraLogs(
            List<LatestNote> notes,
            int[,] noteRefCount,
            int channel,
            ushort previousPacket,
            bool noteOn,
            List<ELog> logs)
        {
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                var count = noteRefCount == null ? 0 : noteRefCount[channel, note.Note];
                if (count < 0)
                {
                    count = 0;
                }

                var logS = note.PacketSequence != previousPacket;
                if (!noteOn && note.Velocity != 64)
                {
                    logs.Add(new ELog
                    {
                        Note = note.Note,
                        VelocityLog = true,
                        Value = note.Velocity,
                        S = logS,
                        Order = note.Order,
                    });
                }

                var countRequired = noteOn ? count > 1 : count > 0;
                if (countRequired)
                {
                    logs.Add(new ELog
                    {
                        Note = note.Note,
                        VelocityLog = false,
                        Value = count >= 127 ? 127 : count,
                        S = logS,
                        Order = note.Order,
                    });
                }
            }
        }

        private static byte[] EncodeChannel(int channel, bool s, byte toc, List<byte> body)
        {
            var length = RtpMidiJournalSection.ChannelJournalHeaderLength + body.Count;
            var bytes = new byte[length];
            bytes[0] = (byte)((s ? 0x80 : 0) | ((channel & 0x0f) << 3) | ((length >> 8) & 0x03));
            bytes[1] = (byte)(length & 0xff);
            bytes[2] = toc;
            body.CopyTo(bytes, 3);
            return bytes;
        }

        private static bool TryApplyChannelNotes(
            byte[] journal,
            ref int cursor,
            int bodyEnd,
            byte toc,
            int channel,
            List<RecoveredMidi> commands)
        {
            var ons = new List<LatestNote>();
            var offs = new List<int>();
            var releaseVelocity = new int[128];
            var stackedCount = new int[128];
            for (var i = 0; i < 128; i++)
            {
                releaseVelocity[i] = 64;
                stackedCount[i] = -1;
            }

            if ((toc & TocN) == TocN)
            {
                if (!TryReadChapterN(journal, ref cursor, bodyEnd, ons, offs))
                {
                    return false;
                }
            }

            if ((toc & TocE) == TocE)
            {
                if (!TryReadChapterE(journal, ref cursor, bodyEnd, releaseVelocity, stackedCount))
                {
                    return false;
                }
            }

            if (ons.Count == 0 && offs.Count == 0)
            {
                return true;
            }

            // Damper timing is not in the journal. Silence sustain unless Chapter C already recovered it.
            var sustainRestored = false;
            for (var i = 0; i < commands.Count; i++)
            {
                if (commands[i].Type == MidiType.ControlChange &&
                    commands[i].Channel == channel &&
                    commands[i].Data1 == 64)
                {
                    sustainRestored = true;
                    break;
                }
            }

            if (!sustainRestored)
            {
                commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, 64, 0));
            }
            for (var i = 0; i < offs.Count; i++)
            {
                var note = offs[i];
                commands.Add(new RecoveredMidi(
                    MidiType.NoteOff,
                    channel,
                    note,
                    releaseVelocity[note],
                    noteRefCount: stackedCount[note]));
            }

            for (var i = 0; i < ons.Count; i++)
            {
                var on = ons[i];
                if (on.Play)
                {
                    commands.Add(new RecoveredMidi(
                        MidiType.NoteOn,
                        channel,
                        on.Note,
                        on.Velocity,
                        noteRefCount: stackedCount[on.Note]));
                }
            }

            return true;
        }

        private static bool TryReadChapterN(byte[] journal, ref int cursor, int bodyEnd, List<LatestNote> ons, List<int> offs)
        {
            if (cursor + 2 > bodyEnd)
            {
                return false;
            }

            var len = journal[cursor] & 0x7f;
            var low = journal[cursor + 1] >> 4;
            var high = journal[cursor + 1] & 0x0f;
            cursor += 2;

            var logCount = len;
            int offOctets;
            if (low == 15 && high == 0)
            {
                offOctets = 0;
                if (len == 127)
                {
                    logCount = 128;
                }
            }
            else if (low == 15 && high == 1)
            {
                offOctets = 0;
            }
            else if (low <= high)
            {
                offOctets = high - low + 1;
            }
            else
            {
                return false;
            }

            if (cursor + (logCount * 2) + offOctets > bodyEnd)
            {
                return false;
            }

            for (var i = 0; i < logCount; i++)
            {
                var note = journal[cursor] & 0x7f;
                var play = (journal[cursor + 1] & 0x80) != 0;
                var velocity = journal[cursor + 1] & 0x7f;
                cursor += 2;
                if (velocity == 0)
                {
                    continue;
                }

                ons.Add(new LatestNote
                {
                    Present = true,
                    IsNoteOn = true,
                    Note = note,
                    Velocity = (byte)velocity,
                    Play = play,
                });
            }

            for (var octet = 0; octet < offOctets; octet++)
            {
                var bits = journal[cursor++];
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((bits & (0x80 >> bit)) != 0)
                    {
                        offs.Add((low + octet) * 8 + bit);
                    }
                }
            }

            return true;
        }

        private static bool TryReadChapterE(byte[] journal, ref int cursor, int bodyEnd, int[] releaseVelocity, int[] stackedCount)
        {
            if (cursor >= bodyEnd)
            {
                return false;
            }

            var logCount = (journal[cursor] & 0x7f) + 1;
            cursor++;
            if (cursor + (logCount * 2) > bodyEnd)
            {
                return false;
            }

            for (var i = 0; i < logCount; i++)
            {
                var note = journal[cursor] & 0x7f;
                var velocityLog = (journal[cursor + 1] & 0x80) != 0;
                var value = journal[cursor + 1] & 0x7f;
                cursor += 2;
                if (velocityLog)
                {
                    releaseVelocity[note] = value;
                }
                else
                {
                    stackedCount[note] = value;
                }
            }

            return true;
        }

        private static bool TryGetNote(byte[] midi, out int channel, out int note, out bool isNoteOn, out byte velocity)
        {
            channel = 0;
            note = 0;
            isNoteOn = false;
            velocity = 0;
            if (midi.Length < 3 || (midi[0] & 0x80) == 0)
            {
                return false;
            }

            var status = midi[0] & 0xf0;
            if (status != 0x80 && status != 0x90)
            {
                return false;
            }

            channel = midi[0] & 0x0f;
            note = midi[1] & 0x7f;
            var rawVelocity = midi[2] & 0x7f;
            if (status == 0x90 && rawVelocity == 0)
            {
                isNoteOn = false;
                velocity = 64;
                return true;
            }

            isNoteOn = status == 0x90;
            velocity = (byte)rawVelocity;
            return true;
        }

        private static bool IsNotesResetController(byte[] midi, out int channel)
        {
            channel = 0;
            if (midi.Length < 3 || (midi[0] & 0xf0) != 0xb0)
            {
                return false;
            }

            var controller = midi[1] & 0x7f;
            if (controller != 120 && (controller < 123 || controller > 127))
            {
                return false;
            }

            channel = midi[0] & 0x0f;
            return true;
        }

        private static bool IsResetState(byte[] midi)
        {
            if (midi.Length == 1 && midi[0] == 0xff)
            {
                return true;
            }

            if (midi.Length == 6 &&
                midi[0] == 0xf0 &&
                midi[1] == 0x7e &&
                midi[5] == 0xf7)
            {
                if (midi[3] == 0x09 && (midi[4] == 0x00 || midi[4] == 0x01 || midi[4] == 0x03))
                {
                    return true;
                }

                if (midi[3] == 0x0a && (midi[4] == 0x01 || midi[4] == 0x02))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsOff(List<LatestNote> offs, int note)
        {
            for (var i = 0; i < offs.Count; i++)
            {
                if (offs[i].Note == note)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearAll(int[,] noteRefCount)
        {
            for (var channel = 0; channel < 16; channel++)
            {
                ClearChannel(noteRefCount, channel);
            }
        }

        private static void ClearChannel(int[,] noteRefCount, int channel)
        {
            for (var note = 0; note < 128; note++)
            {
                noteRefCount[channel, note] = 0;
            }
        }

        private static void ClearLatest(LatestNote[,] latest)
        {
            for (var channel = 0; channel < 16; channel++)
            {
                ClearLatestChannel(latest, channel);
            }
        }

        private static void ClearLatestChannel(LatestNote[,] latest, int channel)
        {
            for (var note = 0; note < 128; note++)
            {
                latest[channel, note] = default;
            }
        }

        private struct LatestNote
        {
            public bool Present;
            public bool IsNoteOn;
            public bool Play;
            public int Note;
            public byte Velocity;
            public ushort PacketSequence;
            public int Order;
        }

        private struct ELog
        {
            public int Note;
            public bool VelocityLog;
            public int Value;
            public bool S;
            public int Order;
        }

        private struct ChannelJournal
        {
            public bool S;
            public byte[] Bytes;
        }
    }
}
