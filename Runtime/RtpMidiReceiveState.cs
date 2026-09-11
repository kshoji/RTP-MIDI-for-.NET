using System.Collections.Generic;

namespace jp.kshoji.rtpmidi
{
    /// <summary>
    /// Receive-side history for differential journal repair (RFC 6295 §4).
    /// Distinct from send-side <see cref="RtpMidiControlState"/> / note reference counts.
    /// </summary>
    public sealed class RtpMidiReceiveState
    {
        private readonly int[,] toggleCount = new int[16, 128];
        private readonly bool[,] switchOn = new bool[16, 128];
        private readonly bool[,] sawToggle = new bool[16, 128];
        private readonly int[,] commandCount = new int[16, 128];
        private readonly bool[,] sawCount = new bool[16, 128];
        private readonly int[,] lastValue = new int[16, 128];
        private readonly bool[,] hasValue = new bool[16, 128];
        private readonly int[,] noteRefCount = new int[16, 128];
        private readonly byte[,] lastOnVelocity = new byte[16, 128];
        private readonly int[] program = new int[16];
        private readonly bool[] hasProgram = new bool[16];
        private readonly int[] pitch = new int[16];
        private readonly bool[] hasPitch = new bool[16];
        private readonly int[] channelPressure = new int[16];
        private readonly bool[] hasChannelPressure = new bool[16];
        private readonly int[,] polyPressure = new int[16, 128];
        private readonly bool[,] hasPoly = new bool[16, 128];
        private int resetCount;
        private int tuneCount;
        private int activeSenseCount;
        private int songSelect = -1;
        private MidiType lastTransport;
        private readonly List<byte> openSysEx = new List<byte>();
        private bool hasOpenSysEx;
        private byte[] lastFinishedSysEx;

        /// <summary>
        /// Turns journal commands into the renderer delta against this state, then observes the emitted commands.
        /// </summary>
        public List<RecoveredMidi> Diff(IReadOnlyList<RecoveredMidi> recovered)
        {
            var emitted = new List<RecoveredMidi>();
            if (recovered == null)
            {
                return emitted;
            }

            var sustainFromChapterC = false;
            for (var i = 0; i < recovered.Count; i++)
            {
                var command = recovered[i];
                if (command.Type == MidiType.ControlChange &&
                    command.Data1 == 64 &&
                    command.ControlTool != RecoveredControlTool.None)
                {
                    sustainFromChapterC = true;
                }
            }

            for (var i = 0; i < recovered.Count; i++)
            {
                AppendDiff(emitted, recovered[i], sustainFromChapterC);
            }

            return emitted;
        }

        /// <summary>
        /// Updates state from a MIDI command section that was played.
        /// </summary>
        public void ObserveMidi(MidiType type, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                ObserveRecovered(new RecoveredMidi(type, 0, 0, 0));
                return;
            }

            var status = data[0];
            if (type == MidiType.NoteOn && data.Length >= 3 && (data[2] & 0x7f) == 0)
            {
                ObserveRecovered(new RecoveredMidi(MidiType.NoteOff, status & 0x0f, data[1] & 0x7f, 64));
                return;
            }

            var channel = status < 0xf0 ? status & 0x0f : 0;
            var data1 = data.Length > 1 ? data[1] & 0x7f : 0;
            var data2 = data.Length > 2 ? data[2] & 0x7f : 0;
            if (type == MidiType.PitchBend && data.Length >= 3)
            {
                ObserveRecovered(new RecoveredMidi(type, channel, data1, data2));
                return;
            }

            ObserveRecovered(new RecoveredMidi(type, channel, data1, data2));
        }

        /// <summary>
        /// Aligns with local All Sound Off / RAC / All Notes Off issued on deep loss or exit.
        /// </summary>
        public void ObserveIndefiniteClear()
        {
            CancelOpenSysEx();
            for (var channel = 0; channel < 16; channel++)
            {
                ObserveRecovered(new RecoveredMidi(
                    MidiType.ControlChange, channel, RtpMidiIndefiniteState.AllSoundOff, 0));
                ObserveRecovered(new RecoveredMidi(
                    MidiType.ControlChange, channel, RtpMidiIndefiniteState.ResetAllControllers, 0));
                ObserveRecovered(new RecoveredMidi(
                    MidiType.ControlChange, channel, RtpMidiIndefiniteState.AllNotesOff, 0));
            }
        }

        private void AppendDiff(List<RecoveredMidi> emitted, RecoveredMidi command, bool sustainFromChapterC)
        {
            switch (command.Type)
            {
                case MidiType.ControlChange:
                    AppendControl(emitted, command, sustainFromChapterC);
                    return;
                case MidiType.NoteOn:
                    AppendNoteOn(emitted, command);
                    return;
                case MidiType.NoteOff:
                    AppendNoteOff(emitted, command);
                    return;
                case MidiType.ProgramChange:
                    if (hasProgram[command.Channel] && program[command.Channel] == command.Data1)
                    {
                        return;
                    }

                    Emit(emitted, command);
                    return;
                case MidiType.PitchBend:
                    var amount = command.Data1 | (command.Data2 << 7);
                    if (hasPitch[command.Channel] && pitch[command.Channel] == amount)
                    {
                        return;
                    }

                    Emit(emitted, command);
                    return;
                case MidiType.AfterTouchChannel:
                    if (hasChannelPressure[command.Channel] && channelPressure[command.Channel] == command.Data1)
                    {
                        return;
                    }

                    Emit(emitted, command);
                    return;
                case MidiType.AfterTouchPoly:
                    if (hasPoly[command.Channel, command.Data1] &&
                        polyPressure[command.Channel, command.Data1] == command.Data2)
                    {
                        return;
                    }

                    Emit(emitted, command);
                    return;
                case MidiType.SystemReset:
                    AppendCounted(emitted, command, ref resetCount, 127);
                    return;
                case MidiType.TuneRequest:
                    AppendCounted(emitted, command, ref tuneCount, 127);
                    return;
                case MidiType.ActiveSensing:
                    AppendCounted(emitted, command, ref activeSenseCount, 127);
                    return;
                case MidiType.SongSelect:
                    if (songSelect == command.Data1)
                    {
                        return;
                    }

                    Emit(emitted, command);
                    return;
                case MidiType.Start:
                case MidiType.Continue:
                case MidiType.Stop:
                    if (lastTransport == command.Type)
                    {
                        return;
                    }

                    Emit(emitted, command);
                    return;
                case MidiType.SystemExclusive:
                    AppendSysEx(emitted, command);
                    return;
                default:
                    Emit(emitted, command);
                    return;
            }
        }

        private void AppendControl(List<RecoveredMidi> emitted, RecoveredMidi command, bool sustainFromChapterC)
        {
            var channel = command.Channel;
            var number = command.Data1;
            if (command.ControlTool == RecoveredControlTool.None && number == 64 && command.Data2 == 0)
            {
                if (sustainFromChapterC || !switchOn[channel, 64])
                {
                    return;
                }

                Emit(emitted, new RecoveredMidi(MidiType.ControlChange, channel, 64, 0));
                return;
            }

            if (command.ControlTool == RecoveredControlTool.Toggle)
            {
                AppendToggle(emitted, channel, number, command.ControlAlt);
                return;
            }

            if (command.ControlTool == RecoveredControlTool.Count)
            {
                AppendCountController(emitted, channel, number, command.ControlAlt);
                return;
            }

            if (hasValue[channel, number] && lastValue[channel, number] == command.Data2)
            {
                return;
            }

            Emit(emitted, command);
        }

        private void AppendToggle(List<RecoveredMidi> emitted, int channel, int number, int journalAlt)
        {
            journalAlt &= 63;
            var local = toggleCount[channel, number];
            var delta = (journalAlt - local) & 63;
            var on = switchOn[channel, number];
            if (delta == 0)
            {
                var journalOn = journalAlt % 2 == 1;
                if (sawToggle[channel, number] && on == journalOn)
                {
                    return;
                }

                if (on == journalOn)
                {
                    toggleCount[channel, number] = journalAlt;
                    sawToggle[channel, number] = true;
                    return;
                }

                delta = 1;
            }

            for (var i = 0; i < delta; i++)
            {
                on = !on;
                Emit(emitted, new RecoveredMidi(MidiType.ControlChange, channel, number, on ? 127 : 0));
            }
        }

        private void AppendCountController(List<RecoveredMidi> emitted, int channel, int number, int journalCount)
        {
            journalCount &= 63;
            var local = commandCount[channel, number];
            var delta = (journalCount - local) & 63;
            if (delta == 0 && sawCount[channel, number])
            {
                return;
            }

            Emit(emitted, new RecoveredMidi(MidiType.ControlChange, channel, number, 0));
            commandCount[channel, number] = journalCount;
            sawCount[channel, number] = true;
        }

        private void AppendNoteOn(List<RecoveredMidi> emitted, RecoveredMidi command)
        {
            var channel = command.Channel;
            var note = command.Data1;
            var velocity = command.Data2 == 0 ? (byte)64 : (byte)command.Data2;
            var target = command.NoteRefCount >= 0 ? command.NoteRefCount : 1;
            if (target < 1)
            {
                target = 1;
            }

            lastOnVelocity[channel, note] = velocity;
            while (noteRefCount[channel, note] < target)
            {
                Emit(emitted, new RecoveredMidi(MidiType.NoteOn, channel, note, velocity));
            }

            while (noteRefCount[channel, note] > target)
            {
                Emit(emitted, new RecoveredMidi(MidiType.NoteOff, channel, note, 64));
            }
        }

        private void AppendNoteOff(List<RecoveredMidi> emitted, RecoveredMidi command)
        {
            var channel = command.Channel;
            var note = command.Data1;
            var velocity = command.Data2;
            var target = command.NoteRefCount >= 0 ? command.NoteRefCount : 0;
            while (noteRefCount[channel, note] > target)
            {
                Emit(emitted, new RecoveredMidi(MidiType.NoteOff, channel, note, velocity));
            }

            var onVelocity = lastOnVelocity[channel, note] == 0 ? (byte)64 : lastOnVelocity[channel, note];
            while (noteRefCount[channel, note] < target)
            {
                Emit(emitted, new RecoveredMidi(MidiType.NoteOn, channel, note, onVelocity));
            }
        }

        private void AppendCounted(List<RecoveredMidi> emitted, RecoveredMidi command, ref int local, int mask)
        {
            var journalCount = command.ControlTool == RecoveredControlTool.Count ? command.ControlAlt & mask : (local + 1) & mask;
            if (journalCount == local)
            {
                return;
            }

            Emit(emitted, command);
            local = journalCount;
        }

        private void Emit(List<RecoveredMidi> emitted, RecoveredMidi command)
        {
            emitted.Add(command);
            ObserveRecovered(command);
        }

        private void ObserveRecovered(RecoveredMidi command)
        {
            switch (command.Type)
            {
                case MidiType.NoteOn:
                    noteRefCount[command.Channel, command.Data1]++;
                    if (command.Data2 > 0)
                    {
                        lastOnVelocity[command.Channel, command.Data1] = (byte)command.Data2;
                    }

                    return;
                case MidiType.NoteOff:
                    if (noteRefCount[command.Channel, command.Data1] > 0)
                    {
                        noteRefCount[command.Channel, command.Data1]--;
                    }

                    return;
                case MidiType.ControlChange:
                    ObserveControl(command.Channel, command.Data1, command.Data2);
                    return;
                case MidiType.ProgramChange:
                    hasProgram[command.Channel] = true;
                    program[command.Channel] = command.Data1;
                    return;
                case MidiType.PitchBend:
                    hasPitch[command.Channel] = true;
                    pitch[command.Channel] = command.Data1 | (command.Data2 << 7);
                    return;
                case MidiType.AfterTouchChannel:
                    hasChannelPressure[command.Channel] = true;
                    channelPressure[command.Channel] = command.Data1;
                    return;
                case MidiType.AfterTouchPoly:
                    hasPoly[command.Channel, command.Data1] = true;
                    polyPressure[command.Channel, command.Data1] = command.Data2;
                    return;
                case MidiType.SystemReset:
                    var nextReset = (resetCount + 1) & 127;
                    ResetAll();
                    resetCount = nextReset;
                    return;
                case MidiType.TuneRequest:
                    tuneCount = (tuneCount + 1) & 127;
                    return;
                case MidiType.ActiveSensing:
                    activeSenseCount = (activeSenseCount + 1) & 127;
                    return;
                case MidiType.SongSelect:
                    songSelect = command.Data1;
                    return;
                case MidiType.Start:
                case MidiType.Continue:
                case MidiType.Stop:
                    lastTransport = command.Type;
                    return;
                case MidiType.SystemExclusive:
                    if (command.Payload != null)
                    {
                        lastFinishedSysEx = command.Payload;
                    }

                    CancelOpenSysEx();
                    return;
            }
        }

        private void ObserveControl(int channel, int number, int value)
        {
            if (number == 121)
            {
                ApplyResetAllControllers(channel);
                commandCount[channel, number] = (commandCount[channel, number] + 1) & 63;
                sawCount[channel, number] = true;
                return;
            }

            if (RtpMidiControlJournal.IsCountController(number))
            {
                commandCount[channel, number] = (commandCount[channel, number] + 1) & 63;
                sawCount[channel, number] = true;
                if (number == 120 || (number >= 123 && number <= 127))
                {
                    ClearChannelNotes(channel);
                }

                return;
            }

            if (RtpMidiControlJournal.IsToggleController(number))
            {
                var nowOn = value >= 64;
                if (nowOn != switchOn[channel, number])
                {
                    toggleCount[channel, number] = (toggleCount[channel, number] + 1) & 63;
                    switchOn[channel, number] = nowOn;
                }

                sawToggle[channel, number] = true;
                return;
            }

            hasValue[channel, number] = true;
            lastValue[channel, number] = value;
        }

        private void ApplyResetAllControllers(int channel)
        {
            hasPitch[channel] = false;
            hasChannelPressure[channel] = false;
            for (var note = 0; note < 128; note++)
            {
                hasPoly[channel, note] = false;
            }

            for (var number = 0; number < 128; number++)
            {
                if (!RtpMidiControlJournal.IsToggleController(number) || !switchOn[channel, number])
                {
                    continue;
                }

                toggleCount[channel, number] = (toggleCount[channel, number] + 1) & 63;
                switchOn[channel, number] = false;
                sawToggle[channel, number] = true;
            }
        }

        private void ClearChannelNotes(int channel)
        {
            for (var note = 0; note < 128; note++)
            {
                noteRefCount[channel, note] = 0;
            }
        }

        private void ResetAll()
        {
            System.Array.Clear(toggleCount, 0, toggleCount.Length);
            System.Array.Clear(switchOn, 0, switchOn.Length);
            System.Array.Clear(sawToggle, 0, sawToggle.Length);
            System.Array.Clear(commandCount, 0, commandCount.Length);
            System.Array.Clear(sawCount, 0, sawCount.Length);
            System.Array.Clear(lastValue, 0, lastValue.Length);
            System.Array.Clear(hasValue, 0, hasValue.Length);
            System.Array.Clear(noteRefCount, 0, noteRefCount.Length);
            System.Array.Clear(lastOnVelocity, 0, lastOnVelocity.Length);
            System.Array.Clear(program, 0, program.Length);
            System.Array.Clear(hasProgram, 0, hasProgram.Length);
            System.Array.Clear(pitch, 0, pitch.Length);
            System.Array.Clear(hasPitch, 0, hasPitch.Length);
            System.Array.Clear(channelPressure, 0, channelPressure.Length);
            System.Array.Clear(hasChannelPressure, 0, hasChannelPressure.Length);
            System.Array.Clear(polyPressure, 0, polyPressure.Length);
            System.Array.Clear(hasPoly, 0, hasPoly.Length);
            tuneCount = 0;
            activeSenseCount = 0;
            songSelect = -1;
            lastTransport = 0;
            CancelOpenSysEx();
            lastFinishedSysEx = null;
        }

        /// <summary>
        /// Consumes a MIDI-section SysEx segment. Returns true and the finished payload when the renderer should play it.
        /// </summary>
        public bool PushSysEx(byte[] midi, out byte[] finished)
        {
            finished = null;
            if (!RtpMidiCommandSection.TryClassifySysEx(midi, out var kind, out var data))
            {
                return false;
            }

            switch (kind)
            {
                case RtpMidiCommandSection.SysExKind.Complete:
                case RtpMidiCommandSection.SysExKind.DroppedF7:
                    CancelOpenSysEx();
                    finished = RtpMidiCommandSection.WrapFinished(data);
                    lastFinishedSysEx = finished;
                    return true;
                case RtpMidiCommandSection.SysExKind.First:
                    openSysEx.Clear();
                    openSysEx.AddRange(data);
                    hasOpenSysEx = true;
                    return false;
                case RtpMidiCommandSection.SysExKind.Middle:
                    if (hasOpenSysEx)
                    {
                        openSysEx.AddRange(data);
                    }

                    return false;
                case RtpMidiCommandSection.SysExKind.Last:
                    if (!hasOpenSysEx)
                    {
                        return false;
                    }

                    openSysEx.AddRange(data);
                    finished = RtpMidiCommandSection.WrapFinished(openSysEx.ToArray());
                    CancelOpenSysEx();
                    lastFinishedSysEx = finished;
                    return true;
                case RtpMidiCommandSection.SysExKind.Cancel:
                    CancelOpenSysEx();
                    return false;
                default:
                    return false;
            }
        }

        private void AppendSysEx(List<RecoveredMidi> emitted, RecoveredMidi command)
        {
            if (command.SysExStatus == 0)
            {
                openSysEx.Clear();
                if (command.Payload != null)
                {
                    openSysEx.AddRange(command.Payload);
                }

                hasOpenSysEx = true;
                return;
            }

            if (command.SysExStatus == 1)
            {
                CancelOpenSysEx();
                return;
            }

            var payload = command.Payload;
            if (payload == null)
            {
                return;
            }

            if (command.SysExStatus == 2 || command.SysExStatus == 3)
            {
                if (payload.Length < 2 || payload[0] != 0xf0)
                {
                    payload = RtpMidiCommandSection.WrapFinished(payload);
                }
            }

            if (SameSysEx(lastFinishedSysEx, payload))
            {
                CancelOpenSysEx();
                return;
            }

            CancelOpenSysEx();
            Emit(emitted, new RecoveredMidi(MidiType.SystemExclusive, 0, 0, 0, payload, sysExStatus: 3));
        }

        private void CancelOpenSysEx()
        {
            openSysEx.Clear();
            hasOpenSysEx = false;
        }

        private static bool SameSysEx(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
