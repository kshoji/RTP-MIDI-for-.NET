using System;
using System.Collections.Generic;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Session-lifetime controller counts for Chapter C toggle and count tools.
    /// Encode must not mutate this state; CommitPending updates it.
    /// </summary>
    public sealed class RtpMidiControlState
    {
        private readonly int[,] toggleCount = new int[16, 128];
        private readonly int[,] commandCount = new int[16, 128];
        private readonly bool[,] switchOn = new bool[16, 128];
        private readonly ChannelParam[] channel = new ChannelParam[16];

        public int ToggleCount(int channel, int controller) => toggleCount[channel, controller];

        public int CommandCount(int channel, int controller) => commandCount[channel, controller];

        /// <summary>
        /// Observes a committed MIDI command and returns how Chapter C / M should treat it.
        /// </summary>
        public RtpMidiControlJournal.ControlMeta Observe(byte[] midi)
        {
            if (midi == null || midi.Length == 0)
            {
                return default;
            }

            if (RtpMidiControlJournal.IsResetState(midi))
            {
                ResetAll();
                return default;
            }

            if (midi.Length < 3 || (midi[0] & 0xf0) != 0xb0)
            {
                return default;
            }

            var ch = midi[0] & 0x0f;
            var number = midi[1] & 0x7f;
            var value = (byte)(midi[2] & 0x7f);
            var meta = new RtpMidiControlJournal.ControlMeta
            {
                IsControlChange = true,
                Channel = ch,
                Number = number,
                Value = value,
            };

            if (number == 121)
            {
                ApplyResetAllControllers(ch);
                commandCount[ch, number] = (commandCount[ch, number] + 1) & 63;
                meta.Kind = RtpMidiControlJournal.LogKind.Count;
                return meta;
            }

            if (number == 99 || number == 101)
            {
                var param = channel[ch];
                param.IsNrpn = number == 99;
                param.Msb = value;
                param.MsbSeen = true;
                param.LsbSeen = false;
                param.HasNumber = false;
                param.NullComplete = false;
                param.Closed = false;
                channel[ch] = param;
                meta.Kind = RtpMidiControlJournal.LogKind.Parameter;
                meta.IsNrpn = param.IsNrpn;
                meta.ParamMsb = value;
                meta.IsParamMsb = true;
                return meta;
            }

            if (number == 98 || number == 100)
            {
                var param = channel[ch];
                var isNrpn = number == 98;
                if (!param.MsbSeen || param.IsNrpn != isNrpn)
                {
                    param.IsNrpn = isNrpn;
                    param.Msb = 0;
                    param.MsbSeen = true;
                }

                param.Lsb = value;
                param.LsbSeen = true;
                param.Closed = false;
                param.NullComplete = param.Msb == 0x7f && value == 0x7f;
                param.HasNumber = !param.NullComplete;
                if (param.HasNumber)
                {
                    if (param.TransactionCounts == null)
                    {
                        param.TransactionCounts = new Dictionary<int, int>();
                    }

                    var key = ParameterKey(param.IsNrpn, param.Msb, param.Lsb);
                    if (!param.TransactionCounts.TryGetValue(key, out var count))
                    {
                        count = 0;
                    }

                    param.TransactionCounts[key] = (count + 1) & 127;
                }

                channel[ch] = param;
                meta.Kind = RtpMidiControlJournal.LogKind.Parameter;
                meta.IsNrpn = param.IsNrpn;
                meta.ParamMsb = param.Msb;
                meta.ParamLsb = param.Lsb;
                meta.ParamNumberComplete = true;
                meta.NullParameter = param.NullComplete;
                meta.IsParamLsb = true;
                return meta;
            }

            if (number == 6 || number == 38 || number == 96 || number == 97)
            {
                var param = channel[ch];
                if (param.HasNumber && !param.Closed && !param.NullComplete)
                {
                    meta.Kind = RtpMidiControlJournal.LogKind.Parameter;
                    meta.IsNrpn = param.IsNrpn;
                    meta.ParamMsb = param.Msb;
                    meta.ParamLsb = param.Lsb;
                    meta.ParamNumberComplete = true;
                    meta.IsDataMsb = number == 6;
                    meta.IsDataLsb = number == 38;
                    meta.IsIncrement = number == 96;
                    meta.IsDecrement = number == 97;
                    return meta;
                }
            }

            if (RtpMidiControlJournal.IsCountController(number))
            {
                commandCount[ch, number] = (commandCount[ch, number] + 1) & 63;
                meta.Kind = RtpMidiControlJournal.LogKind.Count;
                return meta;
            }

            if (RtpMidiControlJournal.IsToggleController(number))
            {
                var nowOn = value >= 64;
                if (nowOn != switchOn[ch, number])
                {
                    toggleCount[ch, number] = (toggleCount[ch, number] + 1) & 63;
                    switchOn[ch, number] = nowOn;
                }

                meta.Kind = RtpMidiControlJournal.LogKind.Toggle;
                return meta;
            }

            meta.Kind = RtpMidiControlJournal.LogKind.Value;
            return meta;
        }

        private void ApplyResetAllControllers(int ch)
        {
            var param = channel[ch];
            param.Closed = true;
            param.HasNumber = false;
            channel[ch] = param;
            for (var number = 0; number < 128; number++)
            {
                if (!RtpMidiControlJournal.IsToggleController(number) || !switchOn[ch, number])
                {
                    continue;
                }

                toggleCount[ch, number] = (toggleCount[ch, number] + 1) & 63;
                switchOn[ch, number] = false;
            }
        }

        private void ResetAll()
        {
            Array.Clear(toggleCount, 0, toggleCount.Length);
            Array.Clear(commandCount, 0, commandCount.Length);
            Array.Clear(switchOn, 0, switchOn.Length);
            for (var i = 0; i < 16; i++)
            {
                channel[i] = default;
            }
        }

        internal int TransactionCount(int channelIndex, bool isNrpn, byte msb, byte lsb)
        {
            var param = channel[channelIndex];
            if (param.TransactionCounts == null)
            {
                return 0;
            }

            return param.TransactionCounts.TryGetValue(ParameterKey(isNrpn, msb, lsb), out var count) ? count : 0;
        }

        private static int ParameterKey(bool isNrpn, byte msb, byte lsb) => (isNrpn ? 1 << 16 : 0) | (msb << 8) | lsb;

        private struct ChannelParam
        {
            public bool IsNrpn;
            public byte Msb;
            public byte Lsb;
            public bool MsbSeen;
            public bool LsbSeen;
            public bool HasNumber;
            public bool NullComplete;
            public bool Closed;
            public Dictionary<int, int> TransactionCounts;
        }
    }

    /// <summary>
    /// Chapter C and Chapter M (RFC 6295 Appendix A.3, A.4). Enhanced Chapter C is not used.
    /// </summary>
    public static class RtpMidiControlJournal
    {
        public enum LogKind
        {
            None,
            Value,
            Toggle,
            Count,
            Parameter,
        }

        public struct ControlMeta
        {
            public bool IsControlChange;
            public int Channel;
            public int Number;
            public byte Value;
            public LogKind Kind;
            public bool IsNrpn;
            public byte ParamMsb;
            public byte ParamLsb;
            public bool ParamNumberComplete;
            public bool NullParameter;
            public bool IsParamMsb;
            public bool IsParamLsb;
            public bool IsDataMsb;
            public bool IsDataLsb;
            public bool IsIncrement;
            public bool IsDecrement;
        }

        public static void EncodeChannel(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            RtpMidiControlState state,
            int channel,
            ushort packetSequenceI,
            ushort checkpointC,
            ChapterPBanks banks,
            out bool s,
            out byte toc,
            out byte[] body)
        {
            s = true;
            toc = 0;
            var chapters = new List<byte>();
            var previous = (ushort)(packetSequenceI - 1);
            if (TryEncodeChapterC(history, state, channel, previous, checkpointC, packetSequenceI, banks, out var chapterC, out var chapterCS))
            {
                chapters.AddRange(chapterC);
                toc |= RtpMidiNoteJournal.TocC;
                if (!chapterCS)
                {
                    s = false;
                }
            }

            if (TryEncodeChapterM(history, state, channel, previous, checkpointC, packetSequenceI, out var chapterM, out var chapterMS))
            {
                chapters.AddRange(chapterM);
                toc |= RtpMidiNoteJournal.TocM;
                if (!chapterMS)
                {
                    s = false;
                }
            }

            body = chapters.ToArray();
        }

        public static bool TryReadChapterC(byte[] journal, ref int cursor, int bodyEnd, int channel, List<RecoveredMidi> commands)
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
                var number = journal[cursor] & 0x7f;
                var second = journal[cursor + 1];
                var altTool = (second & 0x80) != 0;
                cursor += 2;
                int value;
                if (!altTool)
                {
                    value = second & 0x7f;
                }
                else if ((second & 0x40) == 0)
                {
                    value = (second & 0x3f) % 2 == 1 ? 127 : 0;
                }
                else
                {
                    value = 0;
                }

                commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, number, value));
            }

            return true;
        }

        public static bool TryReadChapterM(byte[] journal, ref int cursor, int bodyEnd, int channel, List<RecoveredMidi> commands)
        {
            if (cursor + 2 > bodyEnd)
            {
                return false;
            }

            var first = journal[cursor];
            var length = RtpMidiJournalSection.ReadLength10(journal[cursor], journal[cursor + 1]);
            if (length < 2 || cursor + length > bodyEnd)
            {
                return false;
            }

            var chapterEnd = cursor + length;
            var p = (first & 0x40) == 0x40;
            var e = (first & 0x20) == 0x20;
            cursor += 2;
            int pending = -1;
            var pendingNrpn = false;
            if (p)
            {
                if (cursor >= chapterEnd)
                {
                    return false;
                }

                pendingNrpn = (journal[cursor] & 0x80) == 0x80;
                pending = journal[cursor] & 0x7f;
                cursor++;
            }

            var logs = 0;
            while (cursor + 3 <= chapterEnd)
            {
                if (!TryReadParameterLog(journal, ref cursor, chapterEnd, channel, commands))
                {
                    return false;
                }

                logs++;
            }

            if (cursor != chapterEnd)
            {
                return false;
            }

            if (p && pending >= 0)
            {
                commands.Add(new RecoveredMidi(
                    MidiType.ControlChange,
                    channel,
                    pendingNrpn ? 99 : 101,
                    pending));
            }
            else if (!e && logs == 0)
            {
                commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, 101, 0x7f));
                commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, 100, 0x7f));
            }

            return true;
        }

        private static bool TryEncodeChapterC(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            RtpMidiControlState state,
            int channel,
            ushort previous,
            ushort checkpointC,
            ushort packetSequenceI,
            ChapterPBanks banks,
            out byte[] bytes,
            out bool s)
        {
            s = true;
            bytes = Array.Empty<byte>();
            var latest = new ControllerLog[128];
            var order = 0;
            var lastResetOrder = -1;
            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                if (IsResetState(item.Midi))
                {
                    Array.Clear(latest, 0, latest.Length);
                    lastResetOrder = -1;
                    continue;
                }

                if (!item.Meta.IsControlChange || item.Meta.Channel != channel)
                {
                    continue;
                }

                var thisOrder = order++;
                if (item.Meta.Number == 121)
                {
                    lastResetOrder = thisOrder;
                }

                if (!RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI) ||
                    item.Meta.Kind == LogKind.None ||
                    item.Meta.Kind == LogKind.Parameter)
                {
                    continue;
                }

                latest[item.Meta.Number] = new ControllerLog
                {
                    Present = true,
                    Number = item.Meta.Number,
                    Value = item.Meta.Value,
                    Kind = item.Meta.Kind,
                    PacketSequence = item.PacketSequence,
                    Order = thisOrder,
                    HistoryIndex = i,
                };
            }

            var logs = new List<ControllerLog>();
            for (var number = 0; number < 128; number++)
            {
                if (!latest[number].Present)
                {
                    continue;
                }

                if (OmitBankCodedInChapterP(number, latest[number], banks))
                {
                    continue;
                }

                if (OmitAfterResetAllControllers(number, latest[number].Order, lastResetOrder))
                {
                    continue;
                }

                if (OmitFourteenBitLsb(number, latest))
                {
                    continue;
                }

                if (OmitExclusivePair(number, latest))
                {
                    continue;
                }

                var log = latest[number];
                if (log.Kind == LogKind.Toggle && state != null)
                {
                    log.Alt = state.ToggleCount(channel, number);
                }
                else if (log.Kind == LogKind.Count && state != null)
                {
                    log.Alt = state.CommandCount(channel, number);
                }

                logs.Add(log);
            }

            if (logs.Count == 0)
            {
                return false;
            }

            logs.Sort((a, b) => a.Order.CompareTo(b.Order));
            var encoded = new byte[1 + (logs.Count * 2)];
            var headerS = true;
            for (var i = 0; i < logs.Count; i++)
            {
                if (logs[i].PacketSequence == previous)
                {
                    headerS = false;
                }
            }

            s = headerS;
            encoded[0] = (byte)((headerS ? 0x80 : 0) | ((logs.Count - 1) & 0x7f));
            var offset = 1;
            for (var i = 0; i < logs.Count; i++)
            {
                var log = logs[i];
                var logS = log.PacketSequence != previous;
                var altTool = log.Kind != LogKind.Value;
                encoded[offset++] = (byte)((logS ? 0x80 : 0) | (log.Number & 0x7f));
                if (!altTool)
                {
                    encoded[offset++] = (byte)(log.Value & 0x7f);
                }
                else
                {
                    var t = log.Kind == LogKind.Count ? 0x40 : 0;
                    encoded[offset++] = (byte)(0x80 | t | (log.Alt & 0x3f));
                }
            }

            bytes = encoded;
            return true;
        }

        private static bool TryEncodeChapterM(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            RtpMidiControlState state,
            int channel,
            ushort previous,
            ushort checkpointC,
            ushort packetSequenceI,
            out byte[] bytes,
            out bool s)
        {
            s = true;
            bytes = Array.Empty<byte>();
            var logs = new Dictionary<int, ParameterLog>();
            var order = 0;
            var lastParam = default(ParamEvent);
            var hasParam = false;
            var pending = false;
            var pendingNrpn = false;
            var pendingValue = (byte)0;
            var pendingPacket = (ushort)0;
            var eBit = false;

            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                if (IsResetState(item.Midi))
                {
                    logs.Clear();
                    hasParam = false;
                    pending = false;
                    eBit = false;
                    continue;
                }

                if (item.Meta.IsControlChange && item.Meta.Channel == channel && item.Meta.Number == 121)
                {
                    pending = false;
                    eBit = false;
                    continue;
                }

                if (!item.Meta.IsControlChange || item.Meta.Channel != channel || item.Meta.Kind != LogKind.Parameter)
                {
                    continue;
                }

                var inCheckpoint = RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI);
                hasParam = true;
                lastParam = new ParamEvent { Meta = item.Meta, PacketSequence = item.PacketSequence, Order = order++ };

                if (item.Meta.IsParamMsb)
                {
                    pending = true;
                    pendingNrpn = item.Meta.IsNrpn;
                    pendingValue = item.Meta.ParamMsb;
                    pendingPacket = item.PacketSequence;
                    eBit = false;
                }
                else if (item.Meta.NullParameter)
                {
                    pending = false;
                    eBit = false;
                }
                else if (item.Meta.ParamNumberComplete)
                {
                    pending = false;
                    eBit = true;
                }

                if (!inCheckpoint || item.Meta.NullParameter || !item.Meta.ParamNumberComplete)
                {
                    continue;
                }

                if (!item.Meta.IsDataMsb && !item.Meta.IsDataLsb && !item.Meta.IsIncrement && !item.Meta.IsDecrement && !item.Meta.IsParamLsb)
                {
                    continue;
                }

                var key = ((item.Meta.IsNrpn ? 1 : 0) << 16) | (item.Meta.ParamMsb << 8) | item.Meta.ParamLsb;
                if (!logs.TryGetValue(key, out var log))
                {
                    log = new ParameterLog
                    {
                        IsNrpn = item.Meta.IsNrpn,
                        Msb = item.Meta.ParamMsb,
                        Lsb = item.Meta.ParamLsb,
                        Order = lastParam.Order,
                    };
                }

                log.Order = lastParam.Order;
                log.PacketSequence = item.PacketSequence;
                if (item.Meta.IsDataMsb)
                {
                    log.HasEntryMsb = true;
                    log.EntryMsb = item.Meta.Value;
                    log.EntryMsbOrder = lastParam.Order;
                    if (log.HasEntryLsb && log.EntryLsbOrder < log.EntryMsbOrder)
                    {
                        log.HasEntryLsb = false;
                    }
                }
                else if (item.Meta.IsDataLsb)
                {
                    log.HasEntryLsb = true;
                    log.EntryLsb = item.Meta.Value;
                    log.EntryLsbOrder = lastParam.Order;
                    if (log.HasEntryMsb && log.EntryMsbOrder > log.EntryLsbOrder)
                    {
                        log.HasEntryLsb = false;
                    }
                }
                else if (item.Meta.IsIncrement || item.Meta.IsDecrement)
                {
                    var delta = item.Meta.IsIncrement ? 1 : -1;
                    log.ButtonCount += delta;
                    if (log.ButtonCount > 16383)
                    {
                        log.ButtonCount = 16383;
                    }
                    else if (log.ButtonCount < -16383)
                    {
                        log.ButtonCount = -16383;
                    }

                    log.HasButton = true;
                }
                else if (item.Meta.IsParamLsb)
                {
                    log.InitiatedInCheckpoint = true;
                }

                logs[key] = log;
            }

            var includeHeader = false;
            if (pending)
            {
                includeHeader = RtpMidiJournal.IsInCheckpointHistory(pendingPacket, checkpointC, packetSequenceI);
            }

            if (hasParam && lastParam.Meta.NullParameter &&
                RtpMidiJournal.IsInCheckpointHistory(lastParam.PacketSequence, checkpointC, packetSequenceI))
            {
                includeHeader = true;
            }
            if (logs.Count == 0 && !includeHeader)
            {
                return false;
            }

            if (pending)
            {
                eBit = false;
            }

            var encodedLogs = new List<ParameterLog>(logs.Values);
            encodedLogs.Sort((a, b) => a.Order.CompareTo(b.Order));
            var headerS = true;
            var body = new List<byte>();
            for (var i = 0; i < encodedLogs.Count; i++)
            {
                var log = encodedLogs[i];
                var logS = log.PacketSequence != previous;
                if (!logS)
                {
                    headerS = false;
                }

                var useValue = log.HasEntryMsb || log.HasEntryLsb || log.HasButton;
                var useCount = log.InitiatedInCheckpoint;
                byte toc = 0;
                if (log.HasEntryMsb)
                {
                    toc |= 0x80;
                }

                if (log.HasEntryLsb)
                {
                    toc |= 0x40;
                }

                if (log.HasButton)
                {
                    toc |= 0x20; // L = A-BUTTON
                    toc |= 0x10; // M = C-BUTTON
                }

                if (useCount)
                {
                    toc |= 0x08; // N
                    toc |= 0x04; // T
                }

                if (useValue)
                {
                    toc |= 0x02; // V
                }

                body.Add((byte)((logS ? 0x80 : 0) | (log.Lsb & 0x7f)));
                body.Add((byte)((log.IsNrpn ? 0x80 : 0) | (log.Msb & 0x7f)));
                body.Add(toc);
                if (log.HasEntryMsb)
                {
                    body.Add((byte)(log.EntryMsb & 0x7f));
                }

                if (log.HasEntryLsb)
                {
                    body.Add((byte)(log.EntryLsb & 0x7f));
                }

                if (log.HasButton)
                {
                    WriteButton(body, log.ButtonCount, false);
                    WriteButton(body, log.ButtonCount, false);
                }

                if (useCount)
                {
                    var count = state == null ? 1 : state.TransactionCount(channel, log.IsNrpn, log.Msb, log.Lsb);
                    body.Add((byte)(count & 0x7f));
                }
            }

            if (pending && pendingPacket == previous)
            {
                headerS = false;
            }

            var length = 2 + (pending ? 1 : 0) + body.Count;
            var header = new byte[2 + (pending ? 1 : 0)];
            header[0] = (byte)((headerS ? 0x80 : 0) | (pending ? 0x40 : 0) | (eBit ? 0x20 : 0) | ((length >> 8) & 0x03));
            header[1] = (byte)(length & 0xff);
            if (pending)
            {
                header[2] = (byte)((pendingNrpn ? 0x80 : 0) | (pendingValue & 0x7f));
            }

            bytes = new byte[header.Length + body.Count];
            Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
            body.CopyTo(bytes, header.Length);
            s = headerS;
            return true;
        }

        private static bool TryReadParameterLog(byte[] journal, ref int cursor, int chapterEnd, int channel, List<RecoveredMidi> commands)
        {
            if (cursor + 3 > chapterEnd)
            {
                return false;
            }

            var lsb = journal[cursor] & 0x7f;
            var nrpn = (journal[cursor + 1] & 0x80) == 0x80;
            var msb = journal[cursor + 1] & 0x7f;
            var toc = journal[cursor + 2];
            cursor += 3;
            int entryMsb = -1;
            int entryLsb = -1;
            if ((toc & 0x80) != 0)
            {
                if (cursor >= chapterEnd)
                {
                    return false;
                }

                entryMsb = journal[cursor++] & 0x7f;
            }

            if ((toc & 0x40) != 0)
            {
                if (cursor >= chapterEnd)
                {
                    return false;
                }

                entryLsb = journal[cursor++] & 0x7f;
            }

            if ((toc & 0x20) != 0)
            {
                if (cursor + 2 > chapterEnd)
                {
                    return false;
                }

                cursor += 2;
            }

            if ((toc & 0x10) != 0)
            {
                if (cursor + 2 > chapterEnd)
                {
                    return false;
                }

                cursor += 2;
            }

            if ((toc & 0x08) != 0)
            {
                if (cursor >= chapterEnd)
                {
                    return false;
                }

                cursor++;
            }

            commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, nrpn ? 99 : 101, msb));
            commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, nrpn ? 98 : 100, lsb));
            if (entryMsb >= 0)
            {
                commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, 6, entryMsb));
            }

            if (entryLsb >= 0)
            {
                commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, 38, entryLsb));
            }

            return true;
        }

        private static void WriteButton(List<byte> body, int count, bool x)
        {
            var negative = count < 0;
            var magnitude = Math.Abs(count);
            if (magnitude > 16383)
            {
                magnitude = 16383;
            }

            body.Add((byte)((negative ? 0x80 : 0) | (x ? 0x40 : 0) | ((magnitude >> 8) & 0x3f)));
            body.Add((byte)(magnitude & 0xff));
        }

        private static bool OmitBankCodedInChapterP(int number, ControllerLog log, ChapterPBanks banks)
        {
            if (number == 0 && banks.OmitMsb)
            {
                return log.HistoryIndex == banks.MsbHistoryIndex;
            }

            if (number == 32 && banks.OmitLsb)
            {
                return log.HistoryIndex == banks.LsbHistoryIndex;
            }

            return false;
        }

        private static bool OmitAfterResetAllControllers(int number, int order, int lastResetOrder)
        {
            if (number == 7 || lastResetOrder < 0 || order > lastResetOrder)
            {
                return false;
            }

            return IsRp015Reset(number);
        }

        private static bool OmitFourteenBitLsb(int number, ControllerLog[] latest)
        {
            if (number < 32 || number > 63)
            {
                return false;
            }

            var msb = number - 32;
            if (!IsStandardFourteenBit(msb) || !latest[msb].Present || !latest[number].Present)
            {
                return false;
            }

            return latest[msb].Order > latest[number].Order;
        }

        private static bool OmitExclusivePair(int number, ControllerLog[] latest)
        {
            int other;
            if (number == 124)
            {
                other = 125;
            }
            else if (number == 125)
            {
                other = 124;
            }
            else if (number == 126)
            {
                other = 127;
            }
            else if (number == 127)
            {
                other = 126;
            }
            else
            {
                return false;
            }

            return latest[other].Present && latest[other].Order > latest[number].Order;
        }

        private static bool IsStandardFourteenBit(int msb) =>
            msb == 0 || msb == 1 || msb == 2 || msb == 4 || msb == 5 || msb == 6 || msb == 7 ||
            msb == 8 || msb == 10 || msb == 11 || msb == 12 || msb == 13 || (msb >= 16 && msb <= 19);

        public static bool IsToggleController(int number) =>
            (number >= 64 && number <= 69) || (number >= 80 && number <= 83);

        public static bool IsCountController(int number) =>
            number == 120 || number == 121 || (number >= 123 && number <= 127);

        public static bool IsResetState(byte[] midi)
        {
            if (midi == null || midi.Length == 0)
            {
                return false;
            }

            if (midi.Length == 1 && midi[0] == 0xff)
            {
                return true;
            }

            return midi.Length == 6 && midi[0] == 0xf0 && midi[1] == 0x7e && midi[5] == 0xf7 &&
                   ((midi[3] == 0x09 && (midi[4] == 0x00 || midi[4] == 0x01 || midi[4] == 0x03)) ||
                    (midi[3] == 0x0a && (midi[4] == 0x01 || midi[4] == 0x02)));
        }

        private static bool IsRp015Reset(int number) =>
            number == 1 || number == 2 || number == 4 || number == 5 || number == 8 || number == 11 ||
            (number >= 64 && number <= 67) || number == 84 || (number >= 91 && number <= 95);

        private struct ControllerLog
        {
            public bool Present;
            public int Number;
            public byte Value;
            public int Alt;
            public LogKind Kind;
            public ushort PacketSequence;
            public int Order;
            public int HistoryIndex;
        }

        private struct ParameterLog
        {
            public bool IsNrpn;
            public byte Msb;
            public byte Lsb;
            public int Order;
            public ushort PacketSequence;
            public bool HasEntryMsb;
            public byte EntryMsb;
            public int EntryMsbOrder;
            public bool HasEntryLsb;
            public byte EntryLsb;
            public int EntryLsbOrder;
            public bool HasButton;
            public int ButtonCount;
            public bool InitiatedInCheckpoint;
        }

        private struct ParamEvent
        {
            public ControlMeta Meta;
            public ushort PacketSequence;
            public int Order;
        }
    }
}
