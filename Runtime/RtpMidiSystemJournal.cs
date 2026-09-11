using System;
using System.Collections.Generic;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Session-lifetime system command counts. Survives checkpoint prune.
    /// </summary>
    public sealed class RtpMidiSystemState
    {
        public int ResetCount { get; private set; }
        public int TuneCount { get; private set; }
        public int ActiveSenseCount { get; private set; }
        public int F4Count { get; private set; }
        public int F5Count { get; private set; }
        public int F9Count { get; private set; }
        public int FdCount { get; private set; }

        /// <summary>
        /// Returns the modulo-256 SysEx count after this command if it starts a SysEx, otherwise 0.
        /// </summary>
        public int Observe(byte[] midi)
        {
            if (midi == null || midi.Length == 0)
            {
                return 0;
            }

            if (midi.Length == 1)
            {
                switch (midi[0])
                {
                    case 0xff:
                        ResetCount = (ResetCount + 1) & 0x7f;
                        return 0;
                    case 0xf6:
                        TuneCount = (TuneCount + 1) & 0x7f;
                        return 0;
                    case 0xfe:
                        ActiveSenseCount = (ActiveSenseCount + 1) & 0x7f;
                        return 0;
                    case 0xf4:
                        F4Count = (F4Count + 1) & 0xff;
                        return 0;
                    case 0xf5:
                        F5Count = (F5Count + 1) & 0xff;
                        return 0;
                    case 0xf9:
                        F9Count = (F9Count + 1) & 0xff;
                        return 0;
                    case 0xfd:
                        FdCount = (FdCount + 1) & 0xff;
                        return 0;
                }
            }

            if (midi[0] == 0xf4)
            {
                F4Count = (F4Count + 1) & 0xff;
                return 0;
            }

            if (midi[0] == 0xf5)
            {
                F5Count = (F5Count + 1) & 0xff;
                return 0;
            }

            if (!RtpMidiSystemJournal.StartsSysExCommand(midi))
            {
                return 0;
            }

            var count = (SysExCount + 1) & 0xff;
            SysExCount = count;
            return count == 0 ? 256 : count;
        }

        private int SysExCount { get; set; }
    }

    /// <summary>
    /// System chapters D, V, Q, F, and X (RFC 6295 Appendix B). Encode is read-only.
    /// </summary>
    public static class RtpMidiSystemJournal
    {
        public const byte TocD = 0x40;
        public const byte TocV = 0x20;
        public const byte TocQ = 0x10;
        public const byte TocF = 0x08;
        public const byte TocX = 0x04;

        public static bool TryEncode(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            RtpMidiSystemState state,
            ushort packetSequenceI,
            ushort checkpointC,
            out byte[] journal,
            out bool s)
        {
            journal = null;
            s = true;
            if (history == null)
            {
                return false;
            }

            var previous = (ushort)(packetSequenceI - 1);
            var body = new List<byte>();
            var toc = (byte)0;
            if (TryEncodeChapterD(history, state, previous, checkpointC, packetSequenceI, out var chapterD, out var chapterDS))
            {
                body.AddRange(chapterD);
                toc |= TocD;
                s &= chapterDS;
            }

            if (TryEncodeChapterV(history, state, previous, checkpointC, packetSequenceI, out var chapterV, out var chapterVS))
            {
                body.AddRange(chapterV);
                toc |= TocV;
                s &= chapterVS;
            }

            if (TryEncodeChapterQ(history, previous, checkpointC, packetSequenceI, out var chapterQ, out var chapterQS))
            {
                body.AddRange(chapterQ);
                toc |= TocQ;
                s &= chapterQS;
            }

            if (TryEncodeChapterF(history, previous, checkpointC, packetSequenceI, out var chapterF, out var chapterFS))
            {
                body.AddRange(chapterF);
                toc |= TocF;
                s &= chapterFS;
            }

            if (TryEncodeChapterX(history, previous, checkpointC, packetSequenceI, out var chapterX, out var chapterXS))
            {
                body.AddRange(chapterX);
                toc |= TocX;
                s &= chapterXS;
            }

            if (toc == 0)
            {
                return false;
            }

            var length = 2 + body.Count;
            journal = new byte[length];
            journal[0] = (byte)((s ? 0x80 : 0) | toc | ((length >> 8) & 0x03));
            journal[1] = (byte)(length & 0xff);
            body.CopyTo(journal, 2);
            return true;
        }

        public static bool TryRead(byte[] journal, ref int offset, int packetEnd, List<RecoveredMidi> commands)
        {
            if (offset + 2 > packetEnd)
            {
                return false;
            }

            var first = journal[offset];
            var length = RtpMidiJournalSection.ReadLength10(journal[offset], journal[offset + 1]);
            if (length < 2 || offset + length > packetEnd)
            {
                return false;
            }

            var end = offset + length;
            var cursor = offset + 2;
            var toc = (byte)(first & 0x7c);
            if ((toc & TocD) == TocD && !TryReadChapterD(journal, ref cursor, end, commands))
            {
                return false;
            }

            if ((toc & TocV) == TocV && !TryReadChapterV(journal, ref cursor, end, commands))
            {
                return false;
            }

            if ((toc & TocQ) == TocQ && !TryReadChapterQ(journal, ref cursor, end, commands))
            {
                return false;
            }

            if ((toc & TocF) == TocF && !TryReadChapterF(journal, ref cursor, end, commands))
            {
                return false;
            }

            if ((toc & TocX) == TocX && !TryReadChapterX(journal, ref cursor, end, commands))
            {
                return false;
            }

            if (cursor != end)
            {
                return false;
            }

            offset = end;
            return true;
        }

        public static bool StartsSysExCommand(byte[] midi)
        {
            if (midi == null || midi.Length < 2 || midi[0] != 0xf0)
            {
                return false;
            }

            var last = midi[midi.Length - 1];
            return last == 0xf0 || last == 0xf7 || last == 0xf5;
        }

        private static bool TryEncodeChapterD(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            RtpMidiSystemState state,
            ushort previous,
            ushort checkpointC,
            ushort packetSequenceI,
            out byte[] bytes,
            out bool s)
        {
            s = true;
            bytes = Array.Empty<byte>();
            CountCommand reset = default;
            CountCommand tune = default;
            ValueCommand song = default;
            UndefinedCommand f4 = default;
            UndefinedCommand f5 = default;
            CountCommand f9 = default;
            CountCommand fd = default;
            var resetTotal = 0;
            var tuneTotal = 0;
            var f4Total = 0;
            var f5Total = 0;
            var f9Total = 0;
            var fdTotal = 0;
            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                if (RtpMidiControlJournal.IsResetState(item.Midi) && !IsSystemReset(item.Midi))
                {
                    reset = default;
                    tune = default;
                    song = default;
                    f4 = default;
                    f5 = default;
                    f9 = default;
                    fd = default;
                    continue;
                }

                if (!TryStatus(item.Midi, out var status))
                {
                    continue;
                }

                var inCheckpoint = RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI);
                switch (status)
                {
                    case 0xff:
                        resetTotal = (resetTotal + 1) & 0x7f;
                        reset = new CountCommand
                        {
                            Present = inCheckpoint,
                            Count = resetTotal,
                            Sequence = item.PacketSequence,
                        };
                        break;
                    case 0xf6:
                        tuneTotal = (tuneTotal + 1) & 0x7f;
                        tune = new CountCommand
                        {
                            Present = inCheckpoint,
                            Count = tuneTotal,
                            Sequence = item.PacketSequence,
                        };
                        break;
                    case 0xf3:
                        if (item.Midi.Length >= 2)
                        {
                            song = new ValueCommand
                            {
                                Present = inCheckpoint,
                                Value = item.Midi[1] & 0x7f,
                                Sequence = item.PacketSequence,
                            };
                        }

                        break;
                    case 0xf4:
                        f4Total = (f4Total + 1) & 0xff;
                        f4 = ObserveUndefined(item.Midi, inCheckpoint, item.PacketSequence, f4Total);
                        break;
                    case 0xf5:
                        f5Total = (f5Total + 1) & 0xff;
                        f5 = ObserveUndefined(item.Midi, inCheckpoint, item.PacketSequence, f5Total);
                        break;
                    case 0xf9:
                        f9Total = (f9Total + 1) & 0xff;
                        f9 = new CountCommand
                        {
                            Present = inCheckpoint,
                            Count = f9Total,
                            Sequence = item.PacketSequence,
                        };
                        break;
                    case 0xfd:
                        fdTotal = (fdTotal + 1) & 0xff;
                        fd = new CountCommand
                        {
                            Present = inCheckpoint,
                            Count = fdTotal,
                            Sequence = item.PacketSequence,
                        };
                        break;
                }
            }

            if (state != null)
            {
                if (reset.Present)
                {
                    reset.Count = state.ResetCount;
                }

                if (tune.Present)
                {
                    tune.Count = state.TuneCount;
                }

                if (f4.Present)
                {
                    f4.Count = state.F4Count;
                }

                if (f5.Present)
                {
                    f5.Count = state.F5Count;
                }

                if (f9.Present)
                {
                    f9.Count = state.F9Count;
                }

                if (fd.Present)
                {
                    fd.Count = state.FdCount;
                }
            }

            var logs = new List<byte>();
            var header = (byte)0;
            if (reset.Present)
            {
                header |= 0x40;
                AppendCountLog(logs, reset, previous, ref s);
            }

            if (tune.Present)
            {
                header |= 0x20;
                AppendCountLog(logs, tune, previous, ref s);
            }

            if (song.Present)
            {
                header |= 0x10;
                var logS = song.Sequence != previous;
                s &= logS;
                logs.Add((byte)((logS ? 0x80 : 0) | (song.Value & 0x7f)));
            }

            if (f4.Present)
            {
                header |= 0x08;
                AppendUndefinedCommon(logs, f4, previous, ref s);
            }

            if (f5.Present)
            {
                header |= 0x04;
                AppendUndefinedCommon(logs, f5, previous, ref s);
            }

            if (f9.Present)
            {
                header |= 0x02;
                AppendUndefinedRealtime(logs, f9, previous, ref s);
            }

            if (fd.Present)
            {
                header |= 0x01;
                AppendUndefinedRealtime(logs, fd, previous, ref s);
            }

            if (header == 0)
            {
                return false;
            }

            bytes = new byte[1 + logs.Count];
            bytes[0] = (byte)((s ? 0x80 : 0) | header);
            logs.CopyTo(bytes, 1);
            return true;
        }

        private static bool TryEncodeChapterV(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            RtpMidiSystemState state,
            ushort previous,
            ushort checkpointC,
            ushort packetSequenceI,
            out byte[] bytes,
            out bool s)
        {
            s = true;
            bytes = Array.Empty<byte>();
            var total = 0;
            var present = false;
            ushort sequence = 0;
            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                if (RtpMidiControlJournal.IsResetState(item.Midi))
                {
                    present = false;
                    continue;
                }

                if (!IsStatus(item.Midi, 0xfe))
                {
                    continue;
                }

                total = (total + 1) & 0x7f;
                present = RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI);
                sequence = item.PacketSequence;
            }

            if (!present)
            {
                return false;
            }

            if (state != null)
            {
                total = state.ActiveSenseCount;
            }

            s = sequence != previous;
            bytes = new[] { (byte)((s ? 0x80 : 0) | (total & 0x7f)) };
            return true;
        }

        private static bool TryEncodeChapterQ(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            ushort previous,
            ushort checkpointC,
            ushort packetSequenceI,
            out byte[] bytes,
            out bool s)
        {
            s = true;
            bytes = Array.Empty<byte>();
            var before = new SequencerState();
            var current = new SequencerState();
            var seenCheckpoint = false;
            var checkpointChanged = false;
            ushort changeSequence = 0;
            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                if (RtpMidiControlJournal.IsResetState(item.Midi))
                {
                    current = new SequencerState();
                    if (!seenCheckpoint && RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI))
                    {
                        before = current;
                        seenCheckpoint = true;
                    }

                    continue;
                }

                if (!IsSequencerCommand(item.Midi))
                {
                    continue;
                }

                var inCheckpoint = RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI);
                if (inCheckpoint && !seenCheckpoint)
                {
                    before = current;
                    seenCheckpoint = true;
                }

                var encodedBefore = EncodeSequencer(current);
                ApplySequencer(ref current, item.Midi);
                if (inCheckpoint && EncodeSequencer(current) != encodedBefore)
                {
                    checkpointChanged = true;
                    changeSequence = item.PacketSequence;
                }
            }

            if (!checkpointChanged || EncodeSequencer(current) == EncodeSequencer(before))
            {
                return false;
            }

            s = changeSequence != previous;
            bytes = EncodeChapterQ(current, s);
            return true;
        }

        private static bool TryEncodeChapterF(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            ushort previous,
            ushort checkpointC,
            ushort packetSequenceI,
            out byte[] bytes,
            out bool s)
        {
            s = true;
            bytes = Array.Empty<byte>();
            var tape = new MtcState();
            var include = false;
            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                if (RtpMidiControlJournal.IsResetState(item.Midi))
                {
                    tape = new MtcState();
                    continue;
                }

                var inCheckpoint = RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI);
                if (IsQuarterFrame(item.Midi, out var type, out var nibble))
                {
                    ObserveQuarterFrame(ref tape, type, nibble, item.PacketSequence, inCheckpoint);
                    if (inCheckpoint)
                    {
                        include = true;
                    }
                }
                else if (IsFullFrame(item.Midi, out var hr, out var mn, out var sc, out var fr))
                {
                    tape.HasComplete = true;
                    tape.CompleteFromQuarter = false;
                    tape.Complete = new[] { hr, mn, sc, fr };
                    tape.CompleteSequence = item.PacketSequence;
                    tape.CompleteInCheckpoint = inCheckpoint;
                    tape.PartialInCheckpoint = false;
                    tape.DirectionKnown = false;
                    tape.Reverse = false;
                    if (inCheckpoint)
                    {
                        include = true;
                    }
                }
            }

            if (!include)
            {
                return false;
            }

            var hasComplete = tape.HasComplete && tape.CompleteInCheckpoint;
            var hasPartial = tape.PartialInCheckpoint;
            var reverse = tape.DirectionKnown && tape.Reverse;
            var point = hasPartial ? tape.Point : (reverse ? 0 : 7);
            s = (!hasComplete || tape.CompleteSequence != previous) &&
                (!hasPartial || tape.PartialSequence != previous);
            var header = (byte)((s ? 0x80 : 0) | (hasComplete ? 0x40 : 0) | (hasPartial ? 0x20 : 0) |
                                 (hasComplete && tape.CompleteFromQuarter ? 0x10 : 0) |
                                 (reverse ? 0x08 : 0) | (point & 0x07));
            var body = new List<byte> { header };
            if (hasComplete && tape.Complete != null)
            {
                body.AddRange(tape.Complete);
            }

            if (hasPartial)
            {
                body.AddRange(tape.Partial);
            }

            bytes = body.ToArray();
            return true;
        }

        private static bool TryEncodeChapterX(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            ushort previous,
            ushort checkpointC,
            ushort packetSequenceI,
            out byte[] bytes,
            out bool s)
        {
            s = true;
            bytes = Array.Empty<byte>();
            var logs = CollectSysEx(history, checkpointC, packetSequenceI);
            if (logs.Count == 0)
            {
                return false;
            }

            logs.Sort((a, b) => a.Order.CompareTo(b.Order));
            var encoded = new List<byte>();
            var anyPrevious = false;
            for (var i = 0; i < logs.Count; i++)
            {
                if (logs[i].Sequence == previous)
                {
                    anyPrevious = true;
                }
            }

            s = !anyPrevious;
            for (var i = 0; i < logs.Count; i++)
            {
                var log = logs[i];
                var logS = i == 0 ? s : log.Sequence != previous;
                encoded.Add((byte)((logS ? 0x80 : 0) | 0x20 | (log.Data.Count > 0 ? 0x08 : 0) | (log.Status & 0x03)));
                encoded.Add((byte)(log.Count & 0xff));
                if (log.Data.Count > 0)
                {
                    AppendLengthCoded(encoded, log.Data);
                }
            }

            bytes = encoded.ToArray();
            return true;
        }

        private static List<SysExLog> CollectSysEx(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            ushort checkpointC,
            ushort packetSequenceI)
        {
            var logs = new List<SysExLog>();
            SysExLog open = default;
            var hasOpen = false;
            var order = 0;
            var runningCount = 0;
            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                if (RtpMidiControlJournal.IsResetState(item.Midi))
                {
                    if (hasOpen)
                    {
                        hasOpen = false;
                    }

                    logs.Clear();
                    if (!StartsSysExCommand(item.Midi) || IsFullFrame(item.Midi, out _, out _, out _, out _))
                    {
                        continue;
                    }
                }

                if (!TryClassifySysEx(item.Midi, out var kind, out var data))
                {
                    continue;
                }

                var inCheckpoint = RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI);
                switch (kind)
                {
                    case SysExKind.Verbatim:
                    case SysExKind.DroppedF7:
                        if (IsFullFrame(item.Midi, out _, out _, out _, out _))
                        {
                            runningCount = NextCount(item, runningCount);
                            break;
                        }

                        runningCount = NextCount(item, runningCount);
                        logs.Add(MakeLog(order++, item.PacketSequence, runningCount, data, kind == SysExKind.DroppedF7 ? 2 : 3, inCheckpoint));
                        hasOpen = false;
                        break;
                    case SysExKind.First:
                        runningCount = NextCount(item, runningCount);
                        open = MakeLog(order++, item.PacketSequence, runningCount, data, 0, inCheckpoint);
                        hasOpen = true;
                        break;
                    case SysExKind.Middle:
                        if (!hasOpen || open.Data == null)
                        {
                            break;
                        }

                        open.Data.AddRange(data);
                        if (inCheckpoint)
                        {
                            open.InCheckpoint = true;
                            open.Sequence = item.PacketSequence;
                        }

                        break;
                    case SysExKind.Last:
                        if (!hasOpen || open.Data == null)
                        {
                            break;
                        }

                        open.Data.AddRange(data);
                        open.Status = item.Midi[item.Midi.Length - 1] == 0xf5 ? 2 : 3;
                        if (inCheckpoint)
                        {
                            open.InCheckpoint = true;
                            open.Sequence = item.PacketSequence;
                        }

                        if (open.InCheckpoint)
                        {
                            logs.Add(open);
                        }

                        hasOpen = false;
                        break;
                    case SysExKind.Cancel:
                        if (!hasOpen || open.Data == null)
                        {
                            break;
                        }

                        open.Status = 1;
                        open.Data.Clear();
                        if (inCheckpoint)
                        {
                            open.InCheckpoint = true;
                            open.Sequence = item.PacketSequence;
                        }

                        if (open.InCheckpoint)
                        {
                            logs.Add(open);
                        }

                        hasOpen = false;
                        break;
                }
            }

            if (hasOpen && open.InCheckpoint)
            {
                logs.Add(open);
            }

            logs.RemoveAll(log => !log.InCheckpoint);
            return logs;
        }

        private static int NextCount(RtpMidiNoteJournal.HistoryItem item, int running)
        {
            if (item.SysExCountAfter > 0)
            {
                return item.SysExCountAfter & 0xff;
            }

            return (running + 1) & 0xff;
        }

        private static SysExLog MakeLog(int order, ushort sequence, int count, List<byte> data, int status, bool inCheckpoint)
        {
            return new SysExLog
            {
                Order = order,
                Sequence = sequence,
                Count = count,
                Data = new List<byte>(data),
                Status = status,
                InCheckpoint = inCheckpoint,
            };
        }

        private static bool TryReadChapterD(byte[] journal, ref int cursor, int end, List<RecoveredMidi> commands)
        {
            if (cursor >= end)
            {
                return false;
            }

            var flags = journal[cursor++];
            if ((flags & 0x40) == 0x40)
            {
                if (!ReadCount(journal, ref cursor, end, out var count))
                {
                    return false;
                }

                commands.Add(new RecoveredMidi(
                    MidiType.SystemReset, 0, 0, 0, controlTool: RecoveredControlTool.Count, controlAlt: count));
            }

            if ((flags & 0x20) == 0x20)
            {
                if (!ReadCount(journal, ref cursor, end, out var count))
                {
                    return false;
                }

                commands.Add(new RecoveredMidi(
                    MidiType.TuneRequest, 0, 0, 0, controlTool: RecoveredControlTool.Count, controlAlt: count));
            }

            if ((flags & 0x10) == 0x10)
            {
                if (cursor >= end)
                {
                    return false;
                }

                var song = journal[cursor++] & 0x7f;
                commands.Add(new RecoveredMidi(MidiType.SongSelect, 0, song, 0));
            }

            if ((flags & 0x08) == 0x08 && !SkipUndefinedCommon(journal, ref cursor, end))
            {
                return false;
            }

            if ((flags & 0x04) == 0x04 && !SkipUndefinedCommon(journal, ref cursor, end))
            {
                return false;
            }

            if ((flags & 0x02) == 0x02 && !SkipUndefinedRealtime(journal, ref cursor, end))
            {
                return false;
            }

            if ((flags & 0x01) == 0x01 && !SkipUndefinedRealtime(journal, ref cursor, end))
            {
                return false;
            }

            return true;
        }

        private static bool TryReadChapterV(byte[] journal, ref int cursor, int end, List<RecoveredMidi> commands)
        {
            if (cursor >= end)
            {
                return false;
            }

            var count = journal[cursor] & 0x7f;
            cursor++;
            commands.Add(new RecoveredMidi(
                MidiType.ActiveSensing, 0, 0, 0, controlTool: RecoveredControlTool.Count, controlAlt: count));
            return true;
        }

        private static bool TryReadChapterQ(byte[] journal, ref int cursor, int end, List<RecoveredMidi> commands)
        {
            if (cursor >= end)
            {
                return false;
            }

            var header = journal[cursor++];
            var running = (header & 0x40) == 0x40;
            var hasClock = (header & 0x10) == 0x10;
            var top = header & 0x07;
            var clocks = 0;
            if (hasClock)
            {
                if (cursor + 2 > end)
                {
                    return false;
                }

                clocks = (journal[cursor] << 8) | journal[cursor + 1];
                cursor += 2;
            }

            if ((header & 0x04) == 0x04)
            {
                if (cursor + 3 > end)
                {
                    return false;
                }

                cursor += 3;
            }

            var position = hasClock ? (top << 16) | clocks : 0;
            if (position > 0)
            {
                var beats = position / 6;
                commands.Add(new RecoveredMidi(MidiType.SongPosition, 0, beats & 0x7f, (beats >> 7) & 0x7f));
            }

            if (running)
            {
                var codedContinue = hasClock && (position > 0 || (header & 0x20) == 0);
                commands.Add(new RecoveredMidi(codedContinue ? MidiType.Continue : MidiType.Start, 0, 0, 0));
            }
            else
            {
                commands.Add(new RecoveredMidi(MidiType.Stop, 0, 0, 0));
            }

            return true;
        }

        private static bool TryReadChapterF(byte[] journal, ref int cursor, int end, List<RecoveredMidi> commands)
        {
            if (cursor >= end)
            {
                return false;
            }

            var header = journal[cursor++];
            var hasComplete = (header & 0x40) == 0x40;
            var hasPartial = (header & 0x20) == 0x20;
            var fromQuarter = (header & 0x10) == 0x10;
            var point = header & 0x07;
            if (hasComplete)
            {
                if (cursor + 4 > end)
                {
                    return false;
                }

                if (fromQuarter)
                {
                    EmitQuarterFrames(journal, cursor, 0, 7, commands);
                }
                else
                {
                    var payload = new byte[10];
                    payload[0] = 0xf0;
                    payload[1] = 0x7f;
                    payload[2] = 0x7f;
                    payload[3] = 0x01;
                    payload[4] = 0x01;
                    payload[5] = journal[cursor];
                    payload[6] = journal[cursor + 1];
                    payload[7] = journal[cursor + 2];
                    payload[8] = journal[cursor + 3];
                    payload[9] = 0xf7;
                    commands.Add(new RecoveredMidi(MidiType.SystemExclusive, 0, 0, 0, payload));
                }

                cursor += 4;
            }

            if (hasPartial)
            {
                if (cursor + 4 > end)
                {
                    return false;
                }

                var reverse = (header & 0x08) == 0x08;
                EmitQuarterFrames(journal, cursor, reverse ? point : 0, reverse ? 7 : point, commands);
                cursor += 4;
            }

            return true;
        }

        private static bool TryReadChapterX(byte[] journal, ref int cursor, int end, List<RecoveredMidi> commands)
        {
            while (cursor < end)
            {
                if (!TryReadSysExLog(journal, ref cursor, end, out var status, out var data))
                {
                    return false;
                }

                if (status == 0 || status == 1 || data == null)
                {
                    continue;
                }

                var payload = new byte[data.Length + 2];
                payload[0] = 0xf0;
                for (var i = 0; i < data.Length; i++)
                {
                    payload[i + 1] = (byte)(data[i] & 0x7f);
                }

                payload[payload.Length - 1] = 0xf7;
                commands.Add(new RecoveredMidi(MidiType.SystemExclusive, 0, 0, 0, payload));
            }

            return true;
        }

        private static bool TryReadSysExLog(byte[] journal, ref int cursor, int end, out int status, out byte[] data)
        {
            status = 0;
            data = null;
            if (cursor >= end)
            {
                return false;
            }

            var header = journal[cursor++];
            status = header & 0x03;
            if ((header & 0x40) == 0x40)
            {
                if (cursor >= end)
                {
                    return false;
                }

                cursor++;
            }

            if ((header & 0x20) == 0x20)
            {
                if (cursor >= end)
                {
                    return false;
                }

                cursor++;
            }

            if ((header & 0x10) == 0x10 && !SkipLengthCoded(journal, ref cursor, end))
            {
                return false;
            }

            if ((header & 0x08) == 0x08)
            {
                return ReadLengthCoded(journal, ref cursor, end, out data);
            }

            return true;
        }

        private static void EmitQuarterFrames(byte[] journal, int offset, int from, int to, List<RecoveredMidi> commands)
        {
            for (var type = from; type <= to; type++)
            {
                var nibble = (journal[offset + (type / 2)] >> (type % 2 == 0 ? 4 : 0)) & 0x0f;
                commands.Add(new RecoveredMidi(MidiType.TimeCodeQuarterFrame, 0, (type << 4) | nibble, 0));
            }
        }

        private static void AppendCountLog(List<byte> logs, CountCommand command, ushort previous, ref bool s)
        {
            var logS = command.Sequence != previous;
            s &= logS;
            logs.Add((byte)((logS ? 0x80 : 0) | (command.Count & 0x7f)));
        }

        private static void AppendUndefinedCommon(List<byte> logs, UndefinedCommand command, ushort previous, ref bool s)
        {
            var logS = command.Sequence != previous;
            s &= logS;
            var data = command.Data ?? Array.Empty<byte>();
            var dsz = data.Length >= 3 ? 3 : data.Length;
            var hasCount = dsz == 0;
            var hasValue = dsz != 0;
            var payload = hasCount ? 1 : data.Length;
            var length = 2 + payload;
            logs.Add((byte)((logS ? 0x80 : 0) | (hasCount ? 0x40 : 0) | (hasValue ? 0x20 : 0) | (dsz << 2) | ((length >> 8) & 0x03)));
            logs.Add((byte)(length & 0xff));
            if (hasCount)
            {
                logs.Add((byte)(command.Count & 0xff));
            }
            else
            {
                AppendLengthCoded(logs, data);
            }
        }

        private static void AppendUndefinedRealtime(List<byte> logs, CountCommand command, ushort previous, ref bool s)
        {
            var logS = command.Sequence != previous;
            s &= logS;
            logs.Add((byte)((logS ? 0x80 : 0) | 0x40 | 2));
            logs.Add((byte)(command.Count & 0xff));
        }

        private static UndefinedCommand ObserveUndefined(byte[] midi, bool inCheckpoint, ushort sequence, int count)
        {
            var data = new byte[Math.Max(0, midi.Length - 1)];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(midi[i + 1] & 0x7f);
            }

            return new UndefinedCommand
            {
                Present = inCheckpoint,
                Count = count,
                Sequence = sequence,
                Data = data,
            };
        }

        private static byte[] EncodeChapterQ(SequencerState state, bool s)
        {
            var atStart = state.Clocks == 0;
            var useExplicitZero = state.Running && !state.DownbeatPlayed && atStart && !state.StartMoreRecent;
            var hasClock = !atStart || useExplicitZero;
            var header = (byte)((s ? 0x80 : 0) |
                                 (state.Running ? 0x40 : 0) |
                                 (state.DownbeatPlayed ? 0x20 : 0) |
                                 (hasClock ? 0x10 : 0) |
                                 (hasClock ? (state.Clocks >> 16) & 0x07 : 0));
            if (!hasClock)
            {
                return new[] { header };
            }

            var clock = state.Clocks & 0xffff;
            return new[] { header, (byte)(clock >> 8), (byte)(clock & 0xff) };
        }

        private static int EncodeSequencer(SequencerState state)
        {
            var bytes = EncodeChapterQ(state, true);
            var value = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                value = (value << 8) | bytes[i];
            }

            return value;
        }

        private static void ApplySequencer(ref SequencerState state, byte[] midi)
        {
            switch (midi[0])
            {
                case 0xfa:
                    state.Running = true;
                    state.Clocks = 0;
                    state.DownbeatPlayed = false;
                    state.StartMoreRecent = true;
                    break;
                case 0xfb:
                    state.Running = true;
                    state.StartMoreRecent = false;
                    break;
                case 0xfc:
                    state.Running = false;
                    break;
                case 0xf8:
                    if (!state.Running)
                    {
                        break;
                    }

                    if (!state.DownbeatPlayed)
                    {
                        state.DownbeatPlayed = true;
                    }
                    else
                    {
                        state.Clocks = (state.Clocks + 1) % 524288;
                    }

                    break;
                case 0xf2:
                    if (midi.Length >= 3)
                    {
                        var beats = (midi[1] & 0x7f) | ((midi[2] & 0x7f) << 7);
                        state.Clocks = (beats * 6) % 524288;
                        state.DownbeatPlayed = false;
                    }

                    break;
            }
        }

        private static void ObserveQuarterFrame(ref MtcState tape, int type, int nibble, ushort sequence, bool inCheckpoint)
        {
            if (!tape.Open)
            {
                if (type != 0 && type != 7)
                {
                    tape.PartialInCheckpoint = false;
                    if (inCheckpoint)
                    {
                        tape.IllegalInCheckpoint = true;
                    }

                    return;
                }

                tape.Open = true;
                tape.Reverse = type == 7;
                tape.DirectionKnown = true;
                tape.Nibbles = new int[8];
                tape.Have = new bool[8];
                tape.Expected = type;
            }

            if (type != tape.Expected)
            {
                tape.Open = false;
                tape.PartialInCheckpoint = false;
                ObserveQuarterFrame(ref tape, type, nibble, sequence, inCheckpoint);
                return;
            }

            tape.Nibbles[type] = nibble;
            tape.Have[type] = true;
            tape.Point = type;
            tape.PartialSequence = sequence;
            if (inCheckpoint)
            {
                tape.PartialInCheckpoint = true;
            }

            var completes = (!tape.Reverse && type == 7) || (tape.Reverse && type == 0);
            if (completes && SequenceComplete(tape))
            {
                    var nibbles = tape.Reverse ? (int[])tape.Nibbles.Clone() : OffsetForward(tape.Nibbles);
                    tape.Complete = PackNibbles(nibbles);
                tape.HasComplete = true;
                tape.CompleteFromQuarter = true;
                tape.CompleteSequence = sequence;
                tape.CompleteInCheckpoint = inCheckpoint;
                tape.PartialInCheckpoint = false;
                tape.Open = false;
                return;
            }

            tape.Expected = tape.Reverse ? type - 1 : type + 1;
        }

        private static bool SequenceComplete(MtcState tape)
        {
            if (tape.Reverse)
            {
                for (var type = 7; type >= 0; type--)
                {
                    if (!tape.Have[type])
                    {
                        return false;
                    }
                }
            }
            else
            {
                for (var type = 0; type <= 7; type++)
                {
                    if (!tape.Have[type])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static int[] OffsetForward(int[] nibbles)
        {
            var copy = (int[])nibbles.Clone();
            var rate = (copy[7] >> 1) & 0x03;
            var fps = rate == 0 ? 24 : rate == 1 ? 25 : 30;
            var frames = ((copy[1] & 0x01) << 4) | copy[0];
            var seconds = ((copy[3] & 0x03) << 4) | copy[2];
            var minutes = ((copy[5] & 0x03) << 4) | copy[4];
            var hours = ((copy[7] & 0x01) << 4) | copy[6];
            frames += 2;
            if (frames >= fps)
            {
                frames -= fps;
                seconds++;
                if (seconds >= 60)
                {
                    seconds = 0;
                    minutes++;
                    if (minutes >= 60)
                    {
                        minutes = 0;
                        hours = (hours + 1) & 0x1f;
                    }
                }
            }

            copy[0] = frames & 0x0f;
            copy[1] = (copy[1] & 0x0e) | ((frames >> 4) & 0x01);
            copy[2] = seconds & 0x0f;
            copy[3] = (copy[3] & 0x0c) | ((seconds >> 4) & 0x03);
            copy[4] = minutes & 0x0f;
            copy[5] = (copy[5] & 0x0c) | ((minutes >> 4) & 0x03);
            copy[6] = hours & 0x0f;
            copy[7] = (copy[7] & 0x0e) | ((hours >> 4) & 0x01);
            return copy;
        }

        private static byte[] PackNibbles(int[] nibbles)
        {
            var packed = new byte[4];
            for (var type = 0; type < 8; type++)
            {
                var shift = type % 2 == 0 ? 4 : 0;
                packed[type / 2] |= (byte)((nibbles[type] & 0x0f) << shift);
            }

            return packed;
        }

        private static void AppendLengthCoded(List<byte> target, IReadOnlyList<byte> data)
        {
            for (var i = 0; i < data.Count; i++)
            {
                var octet = (byte)(data[i] & 0x7f);
                if (i == data.Count - 1)
                {
                    octet |= 0x80;
                }

                target.Add(octet);
            }
        }

        private static bool ReadLengthCoded(byte[] journal, ref int cursor, int end, out byte[] data)
        {
            var values = new List<byte>();
            while (cursor < end)
            {
                var octet = journal[cursor++];
                values.Add((byte)(octet & 0x7f));
                if ((octet & 0x80) == 0x80)
                {
                    data = values.ToArray();
                    return true;
                }
            }

            data = null;
            return false;
        }

        private static bool SkipLengthCoded(byte[] journal, ref int cursor, int end)
        {
            while (cursor < end)
            {
                if ((journal[cursor++] & 0x80) == 0x80)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReadCount(byte[] journal, ref int cursor, int end, out int count)
        {
            count = 0;
            if (cursor >= end)
            {
                return false;
            }

            count = journal[cursor++] & 0x7f;
            return true;
        }

        private static bool SkipUndefinedCommon(byte[] journal, ref int cursor, int end)
        {
            if (cursor + 2 > end)
            {
                return false;
            }

            var length = RtpMidiJournalSection.ReadLength10(journal[cursor], journal[cursor + 1]);
            if (length < 2 || cursor + length > end)
            {
                return false;
            }

            cursor += length;
            return true;
        }

        private static bool SkipUndefinedRealtime(byte[] journal, ref int cursor, int end)
        {
            if (cursor >= end)
            {
                return false;
            }

            var length = journal[cursor] & 0x1f;
            if (length < 1 || cursor + length > end)
            {
                return false;
            }

            cursor += length;
            return true;
        }

        private static bool TryClassifySysEx(byte[] midi, out SysExKind kind, out List<byte> data)
        {
            kind = SysExKind.None;
            data = new List<byte>();
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
                kind = SysExKind.Verbatim;
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
            else if (first == 0xf7 && last == 0xf7)
            {
                kind = SysExKind.Last;
            }
            else if (first == 0xf7 && last == 0xf5)
            {
                kind = SysExKind.Last;
            }
            else
            {
                return false;
            }

            for (var i = 1; i < midi.Length - 1; i++)
            {
                data.Add((byte)(midi[i] & 0x7f));
            }

            if (first == 0xf7 && last == 0xf5)
            {
                kind = SysExKind.Last;
            }

            return true;
        }

        private static bool IsFullFrame(byte[] midi, out byte hr, out byte mn, out byte sc, out byte fr)
        {
            hr = mn = sc = fr = 0;
            if (midi == null || midi.Length != 10 || midi[0] != 0xf0 || midi[1] != 0x7f || midi[3] != 0x01 ||
                midi[4] != 0x01 || midi[9] != 0xf7)
            {
                return false;
            }

            hr = midi[5];
            mn = midi[6];
            sc = midi[7];
            fr = midi[8];
            return true;
        }

        private static bool IsQuarterFrame(byte[] midi, out int type, out int nibble)
        {
            type = 0;
            nibble = 0;
            if (midi == null || midi.Length < 2 || midi[0] != 0xf1)
            {
                return false;
            }

            type = (midi[1] >> 4) & 0x07;
            nibble = midi[1] & 0x0f;
            return true;
        }

        private static bool IsSequencerCommand(byte[] midi)
        {
            if (midi == null || midi.Length == 0)
            {
                return false;
            }

            switch (midi[0])
            {
                case 0xf2:
                    return midi.Length >= 3;
                case 0xf8:
                case 0xfa:
                case 0xfb:
                case 0xfc:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSystemReset(byte[] midi) => midi != null && midi.Length == 1 && midi[0] == 0xff;

        private static bool IsStatus(byte[] midi, byte status) => midi != null && midi.Length >= 1 && midi[0] == status;

        private static bool TryStatus(byte[] midi, out byte status)
        {
            status = 0;
            if (midi == null || midi.Length == 0 || midi[0] < 0xf0)
            {
                return false;
            }

            status = midi[0];
            return true;
        }

        private enum SysExKind
        {
            None,
            Verbatim,
            First,
            Middle,
            Last,
            Cancel,
            DroppedF7,
        }

        private struct CountCommand
        {
            public bool Present;
            public int Count;
            public ushort Sequence;
        }

        private struct ValueCommand
        {
            public bool Present;
            public int Value;
            public ushort Sequence;
        }

        private struct UndefinedCommand
        {
            public bool Present;
            public int Count;
            public ushort Sequence;
            public byte[] Data;
        }

        private struct SequencerState
        {
            public bool Running;
            public bool DownbeatPlayed;
            public bool StartMoreRecent;
            public int Clocks;
        }

        private struct MtcState
        {
            public bool Open;
            public bool Reverse;
            public bool DirectionKnown;
            public int Expected;
            public int Point;
            public int[] Nibbles;
            public bool[] Have;
            public bool HasComplete;
            public bool CompleteFromQuarter;
            public byte[] Complete;
            public ushort CompleteSequence;
            public bool CompleteInCheckpoint;
            public bool PartialInCheckpoint;
            public ushort PartialSequence;
            public bool IllegalInCheckpoint;
            public byte[] Partial
            {
                get
                {
                    var packed = new int[8];
                    if (Nibbles == null)
                    {
                        return new byte[4];
                    }

                    if (Reverse)
                    {
                        for (var type = 7; type >= Point; type--)
                        {
                            packed[type] = Nibbles[type];
                        }
                    }
                    else
                    {
                        for (var type = 0; type <= Point; type++)
                        {
                            packed[type] = Nibbles[type];
                        }
                    }

                    return PackNibbles(packed);
                }
            }
        }

        private struct SysExLog
        {
            public int Order;
            public ushort Sequence;
            public int Count;
            public List<byte> Data;
            public int Status;
            public bool InCheckpoint;
        }
    }
}
