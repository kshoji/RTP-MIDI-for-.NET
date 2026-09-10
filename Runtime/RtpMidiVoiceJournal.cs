using System;
using System.Collections.Generic;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Bank Select instances coded in Chapter P. Chapter C may omit those instances.
    /// </summary>
    public struct ChapterPBanks
    {
        public bool OmitMsb;
        public int MsbHistoryIndex;
        public bool OmitLsb;
        public int LsbHistoryIndex;
    }

    /// <summary>
    /// Chapters P, W, T, and A (RFC 6295 Appendix A.2, A.5, A.8, A.9). Encode is read-only.
    /// </summary>
    public static class RtpMidiVoiceJournal
    {
        public static void EncodeChannel(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            int channel,
            ushort packetSequenceI,
            ushort checkpointC,
            out bool s,
            out byte toc,
            out byte[] chapterP,
            out byte[] chapterW,
            out byte[] chapterTail,
            out ChapterPBanks banks)
        {
            s = true;
            toc = 0;
            chapterP = Array.Empty<byte>();
            chapterW = Array.Empty<byte>();
            chapterTail = Array.Empty<byte>();
            banks = default;
            if (history == null)
            {
                return;
            }

            var scan = Scan(history, channel, checkpointC, packetSequenceI);
            var previous = (ushort)(packetSequenceI - 1);
            var tail = new List<byte>();
            if (scan.Program.Present)
            {
                chapterP = EncodeChapterP(scan, previous, out var chapterPS);
                toc |= RtpMidiNoteJournal.TocP;
                if (!chapterPS)
                {
                    s = false;
                }

                banks = scan.Banks;
            }

            if (scan.Pitch.Present)
            {
                chapterW = EncodeChapterW(scan.Pitch, previous, out var chapterWS);
                toc |= RtpMidiNoteJournal.TocW;
                if (!chapterWS)
                {
                    s = false;
                }
            }

            if (scan.ChannelPressure.Present)
            {
                var chapterTS = scan.ChannelPressure.Sequence != previous;
                tail.Add((byte)((chapterTS ? 0x80 : 0) | (scan.ChannelPressure.Data1 & 0x7f)));
                toc |= RtpMidiNoteJournal.TocT;
                if (!chapterTS)
                {
                    s = false;
                }
            }

            if (scan.Poly.Count > 0)
            {
                tail.AddRange(EncodeChapterA(scan.Poly, previous, out var chapterAS));
                toc |= RtpMidiNoteJournal.TocA;
                if (!chapterAS)
                {
                    s = false;
                }
            }

            chapterTail = tail.ToArray();
        }

        public static bool TryReadChapterP(byte[] journal, ref int cursor, int bodyEnd, int channel, List<RecoveredMidi> commands)
        {
            if (cursor + 3 > bodyEnd)
            {
                return false;
            }

            var program = journal[cursor] & 0x7f;
            var bankMsb = journal[cursor + 1];
            var bankLsb = journal[cursor + 2] & 0x7f;
            cursor += 3;
            if ((bankMsb & 0x80) == 0x80)
            {
                commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, 0, bankMsb & 0x7f));
                commands.Add(new RecoveredMidi(MidiType.ControlChange, channel, 32, bankLsb));
            }

            commands.Add(new RecoveredMidi(MidiType.ProgramChange, channel, program, 0));
            return true;
        }

        public static bool TryReadChapterW(byte[] journal, ref int cursor, int bodyEnd, int channel, List<RecoveredMidi> commands)
        {
            if (cursor + 2 > bodyEnd)
            {
                return false;
            }

            var first = journal[cursor] & 0x7f;
            var second = journal[cursor + 1] & 0x7f;
            cursor += 2;
            commands.Add(new RecoveredMidi(MidiType.PitchBend, channel, first, second));
            return true;
        }

        public static bool TryReadChapterT(byte[] journal, ref int cursor, int bodyEnd, int channel, List<RecoveredMidi> commands)
        {
            if (cursor >= bodyEnd)
            {
                return false;
            }

            var pressure = journal[cursor] & 0x7f;
            cursor++;
            commands.Add(new RecoveredMidi(MidiType.AfterTouchChannel, channel, pressure, 0));
            return true;
        }

        public static bool TryReadChapterA(byte[] journal, ref int cursor, int bodyEnd, int channel, List<RecoveredMidi> commands)
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
                var pressure = journal[cursor + 1] & 0x7f;
                cursor += 2;
                commands.Add(new RecoveredMidi(MidiType.AfterTouchPoly, channel, note, pressure));
            }

            return true;
        }

        private static byte[] EncodeChapterP(ScanState scan, ushort previous, out bool s)
        {
            var program = scan.Program;
            s = program.Sequence != previous &&
                (!scan.HasBankMsb || scan.BankMsbSequence != previous) &&
                (!scan.HasBankLsb || scan.BankLsbSequence != previous);
            return new[]
            {
                (byte)((s ? 0x80 : 0) | (program.Data1 & 0x7f)),
                (byte)((scan.HasBankMsb ? 0x80 : 0) | (scan.BankMsb & 0x7f)),
                (byte)((scan.BankX ? 0x80 : 0) | (scan.BankLsb & 0x7f)),
            };
        }

        private static byte[] EncodeChapterW(TimedCommand pitch, ushort previous, out bool s)
        {
            s = pitch.Sequence != previous;
            return new[]
            {
                (byte)((s ? 0x80 : 0) | (pitch.Data1 & 0x7f)),
                (byte)(pitch.Data2 & 0x7f),
            };
        }

        private static byte[] EncodeChapterA(List<TimedCommand> logs, ushort previous, out bool s)
        {
            logs.Sort((a, b) => a.Order.CompareTo(b.Order));
            s = true;
            var bytes = new byte[1 + (logs.Count * 2)];
            var offset = 1;
            for (var i = 0; i < logs.Count; i++)
            {
                var log = logs[i];
                var logS = log.Sequence != previous;
                if (!logS)
                {
                    s = false;
                }

                bytes[offset++] = (byte)((logS ? 0x80 : 0) | (log.Data1 & 0x7f));
                bytes[offset++] = (byte)((log.BeforeNotesOff ? 0x80 : 0) | (log.Data2 & 0x7f));
            }

            bytes[0] = (byte)((s ? 0x80 : 0) | ((logs.Count - 1) & 0x7f));
            return bytes;
        }

        private static ScanState Scan(
            IReadOnlyList<RtpMidiNoteJournal.HistoryItem> history,
            int channel,
            ushort checkpointC,
            ushort packetSequenceI)
        {
            var scan = NewScan();
            var running = new RunningBank();
            var poly = new TimedCommand[128];
            var notesOffOrder = -1;
            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];
                if (RtpMidiControlJournal.IsResetState(item.Midi))
                {
                    scan = NewScan();
                    running = new RunningBank();
                    Array.Clear(poly, 0, poly.Length);
                    notesOffOrder = -1;
                    continue;
                }

                if (!TryParse(item.Midi, out var midiChannel, out var type, out var data1, out var data2) ||
                    midiChannel != channel)
                {
                    continue;
                }

                var order = scan.NextOrder++;
                var inCheckpoint = RtpMidiJournal.IsInCheckpointHistory(item.PacketSequence, checkpointC, packetSequenceI);
                switch (type)
                {
                    case MidiType.ControlChange:
                        ObserveController(data1, data2, item.PacketSequence, i, ref running, ref scan, poly, ref notesOffOrder);
                        break;
                    case MidiType.ProgramChange:
                        ObserveProgram(data1, item.PacketSequence, i, inCheckpoint, ref running, ref scan);
                        break;
                    case MidiType.PitchBend:
                        scan.Pitch = Command(data1, data2, item.PacketSequence, order, inCheckpoint);
                        break;
                    case MidiType.AfterTouchChannel:
                        scan.ChannelPressure = Command(data1, 0, item.PacketSequence, order, inCheckpoint);
                        break;
                    case MidiType.AfterTouchPoly:
                        poly[data1] = Command(data1, data2, item.PacketSequence, order, inCheckpoint);
                        break;
                }
            }

            FinishPoly(scan, poly, notesOffOrder);
            return scan;
        }

        private static void ObserveController(
            int number,
            int value,
            ushort sequence,
            int historyIndex,
            ref RunningBank running,
            ref ScanState scan,
            TimedCommand[] poly,
            ref int notesOffOrder)
        {
            if (number == 0)
            {
                running.HasMsb = true;
                running.Msb = (byte)value;
                running.MsbSequence = sequence;
                running.MsbHistoryIndex = historyIndex;
                running.HasLsb = false;
                running.Cc121SinceMsb = false;
                return;
            }

            if (number == 32 && running.HasMsb)
            {
                running.HasLsb = true;
                running.Lsb = (byte)value;
                running.LsbSequence = sequence;
                running.LsbHistoryIndex = historyIndex;
                return;
            }

            if (number == 121)
            {
                scan.Pitch = default;
                scan.ChannelPressure = default;
                Array.Clear(poly, 0, poly.Length);
                if (running.HasMsb)
                {
                    running.Cc121SinceMsb = true;
                }

                return;
            }

            if (number == 120 || (number >= 123 && number <= 127))
            {
                scan.ChannelPressure = default;
                notesOffOrder = scan.NextOrder - 1;
            }
        }

        private static void ObserveProgram(
            int program,
            ushort sequence,
            int historyIndex,
            bool inCheckpoint,
            ref RunningBank running,
            ref ScanState scan)
        {
            scan.Program = new TimedCommand();
            scan.HasBankMsb = false;
            scan.HasBankLsb = false;
            scan.BankX = false;
            scan.Banks = default;
            if (!inCheckpoint)
            {
                return;
            }

            scan.Program = Command(program, 0, sequence, historyIndex, true);
            if (!running.HasMsb)
            {
                return;
            }

            scan.HasBankMsb = true;
            scan.BankMsb = running.Msb;
            scan.BankMsbSequence = running.MsbSequence;
            scan.BankX = running.Cc121SinceMsb;
            scan.Banks.OmitMsb = true;
            scan.Banks.MsbHistoryIndex = running.MsbHistoryIndex;
            if (running.HasLsb)
            {
                scan.HasBankLsb = true;
                scan.BankLsb = running.Lsb;
                scan.BankLsbSequence = running.LsbSequence;
                scan.Banks.OmitLsb = true;
                scan.Banks.LsbHistoryIndex = running.LsbHistoryIndex;
            }
        }

        private static void FinishPoly(ScanState scan, TimedCommand[] poly, int notesOffOrder)
        {
            for (var note = 0; note < poly.Length; note++)
            {
                if (!poly[note].Present)
                {
                    continue;
                }

                var log = poly[note];
                log.BeforeNotesOff = notesOffOrder >= 0 && log.Order < notesOffOrder;
                scan.Poly.Add(log);
            }
        }

        private static ScanState NewScan()
        {
            return new ScanState { Poly = new List<TimedCommand>() };
        }

        private static TimedCommand Command(int data1, int data2, ushort sequence, int order, bool inCheckpoint)
        {
            return new TimedCommand
            {
                Present = inCheckpoint,
                Data1 = data1,
                Data2 = data2,
                Sequence = sequence,
                Order = order,
            };
        }

        private static bool TryParse(byte[] midi, out int channel, out MidiType type, out int data1, out int data2)
        {
            channel = 0;
            type = 0;
            data1 = 0;
            data2 = 0;
            if (midi == null || midi.Length == 0)
            {
                return false;
            }

            var status = midi[0];
            if (status < 0x80 || status >= 0xf0)
            {
                return false;
            }

            type = (MidiType)(status & 0xf0);
            channel = status & 0x0f;
            switch (type)
            {
                case MidiType.ProgramChange:
                case MidiType.AfterTouchChannel:
                    if (midi.Length < 2)
                    {
                        return false;
                    }

                    data1 = midi[1] & 0x7f;
                    return true;
                case MidiType.ControlChange:
                case MidiType.PitchBend:
                case MidiType.AfterTouchPoly:
                    if (midi.Length < 3)
                    {
                        return false;
                    }

                    data1 = midi[1] & 0x7f;
                    data2 = midi[2] & 0x7f;
                    return true;
                default:
                    return false;
            }
        }

        private struct RunningBank
        {
            public bool HasMsb;
            public byte Msb;
            public ushort MsbSequence;
            public int MsbHistoryIndex;
            public bool HasLsb;
            public byte Lsb;
            public ushort LsbSequence;
            public int LsbHistoryIndex;
            public bool Cc121SinceMsb;
        }

        private struct ScanState
        {
            public int NextOrder;
            public TimedCommand Program;
            public bool HasBankMsb;
            public byte BankMsb;
            public ushort BankMsbSequence;
            public bool HasBankLsb;
            public byte BankLsb;
            public ushort BankLsbSequence;
            public bool BankX;
            public ChapterPBanks Banks;
            public TimedCommand Pitch;
            public TimedCommand ChannelPressure;
            public List<TimedCommand> Poly;
        }

        private struct TimedCommand
        {
            public bool Present;
            public int Data1;
            public int Data2;
            public ushort Sequence;
            public int Order;
            public bool BeforeNotesOff;
        }
    }
}
