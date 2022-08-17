#if ENABLE_RTP_MIDI_JOURNAL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace jp.kshoji.rtpmidi
{
    public class RtpMidiJournal
    {
        private short sequenceNumber;

        public interface IRtpMidiJournalChapter
        {
            byte GetChapterTypeFlag();
            byte[] GetChapterData();
            void SetChapterData(IRtpMidiJournalChapter journal);
        }
        
        /// <summary>
        /// Chapter P: Program Change
        /// </summary>
        internal class RtpMidiJournalChapterProgramChange : IRtpMidiJournalChapter
        {
            private byte program;
            private byte bankMsb; // Control Change #0
            private byte bankLsb; // Control Change #32
            private byte nextProgram;
            private byte nextBankMsb; // Control Change #0
            private byte nextBankLsb; // Control Change #32

            internal RtpMidiJournalChapterProgramChange() { }
            internal RtpMidiJournalChapterProgramChange(byte? program, byte? bankMsb, byte? bankLsb)
            {
                if (program.HasValue)
                {
                    nextProgram = program.Value;
                }
                if (bankMsb.HasValue)
                {
                    nextBankMsb = bankMsb.Value;
                }
                if (bankLsb.HasValue)
                {
                    nextBankLsb = bankLsb.Value;
                }
            }

            private readonly byte[] chapterData = new byte[3];
            public byte GetChapterTypeFlag()
            {
                return 0x80;
            }

            public byte[] GetChapterData()
            {
                // if same data, return no chapter
                if (program == nextProgram && bankMsb == nextBankMsb && bankLsb == nextBankLsb)
                {
                    return null;
                }

                // |S|program|B|bankmsb|X|banklsb|
                if (program != nextProgram)
                {
                    chapterData[0] |= 0x80;
                    program = nextProgram;
                }
                else
                {
                    chapterData[0] &= 0x7f;
                }
                chapterData[0] |= (byte)(program & 0x7f);

                if (bankMsb != nextBankMsb)
                {
                    chapterData[1] |= 0x80;
                    bankMsb = nextBankMsb;
                }
                else
                {
                    chapterData[1] &= 0x7f;
                }
                chapterData[1] |= (byte)(bankMsb & 0x7f);

                if (bankLsb != nextBankLsb)
                {
                    chapterData[2] |= 0x80;
                    bankLsb = nextBankLsb;
                }
                else
                {
                    chapterData[2] &= 0x7f;
                }
                chapterData[2] |= (byte)(bankLsb & 0x7f);

                return chapterData;
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterProgramChange chapterProgramChange)
                {
                    nextProgram = chapterProgramChange.nextProgram;
                    nextBankMsb = chapterProgramChange.nextBankMsb;
                    nextBankLsb = chapterProgramChange.nextBankLsb;
                }
            }
        }

        /// <summary>
        /// Chapter C: Control Change
        /// </summary>
        internal class RtpMidiJournalChapterControlChange : IRtpMidiJournalChapter
        {
            readonly List<KeyValuePair<byte, byte>> controlChangeLog = new List<KeyValuePair<byte, byte>>();
            private byte number;
            private byte value;
            public byte GetChapterTypeFlag()
            {
                return 0x40;
            }

            internal RtpMidiJournalChapterControlChange() { }
            internal RtpMidiJournalChapterControlChange(byte number, byte value)
            {
                this.number = number;
                this.value = value;
            }

            public byte[] GetChapterData()
            {
                var length = controlChangeLog.Count;
                if (length == 0)
                {
                    return null;
                }
                length--;

                var dataStream = new MemoryStream();
                dataStream.Write(new []
                {
                    (byte)length,
                }, 0, 1);

                foreach (var controlChange in controlChangeLog)
                {
                    dataStream.Write(new []
                    {
                        (byte)(controlChange.Key & 0x7f),
                        (byte)(controlChange.Value & 0x7f),
                    }, 0, 2);
                }

                return dataStream.ToArray();
            }

            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterControlChange chapterControlChange)
                {
                    // Remove control from log
                    controlChangeLog.RemoveAll(x => x.Key == chapterControlChange.number);

                    // number 124(Omni Off), 125(Omni On) pair is exclusive
                    if (chapterControlChange.number == 124)
                    {
                        controlChangeLog.RemoveAll(x => x.Key == 125);
                    }
                    if (chapterControlChange.number == 125)
                    {
                        controlChangeLog.RemoveAll(x => x.Key == 124);
                    }

                    // number 126(Mono), 127(Poly) pair is exclusive
                    if (chapterControlChange.number == 126)
                    {
                        controlChangeLog.RemoveAll(x => x.Key == 127);
                    }
                    if (chapterControlChange.number == 127)
                    {
                        controlChangeLog.RemoveAll(x => x.Key == 126);
                    }

                    // Add to latest log
                    controlChangeLog.Add(new KeyValuePair<byte, byte>(chapterControlChange.number, chapterControlChange.value));
                }
            }
        }

        /// <summary>
        /// Chapter M: Parameter System
        /// RPN, NRPN
        /// </summary>
        internal class RtpMidiJournalChapterParameterSystem : IRtpMidiJournalChapter
        {
            public byte GetChapterTypeFlag()
            {
                return 0x20;
            }
            public byte[] GetChapterData()
            {
                throw new NotImplementedException();
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Chapter W: Pitch Wheel
        /// </summary>
        internal class RtpMidiJournalChapterPitchWheel : IRtpMidiJournalChapter
        {
            private short pitchWheel;
            private short nextPitchWheel;
            public byte GetChapterTypeFlag()
            {
                return 0x10;
            }
            internal RtpMidiJournalChapterPitchWheel() { }
            internal RtpMidiJournalChapterPitchWheel(short pitchWheel)
            {
                nextPitchWheel = pitchWheel;
            }
            public byte[] GetChapterData()
            {
                if (nextPitchWheel == pitchWheel)
                {
                    return null;
                }

                pitchWheel = nextPitchWheel;
                return new[] {(byte)((pitchWheel >> 7) & 0x7f), (byte)(pitchWheel & 0x7f)};
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterPitchWheel chapterPitchWheel)
                {
                    nextPitchWheel = chapterPitchWheel.nextPitchWheel;
                }
            }
        }

        /// <summary>
        /// Chapter N: Note Off/On
        /// </summary>
        internal class RtpMidiJournalChapterNote : IRtpMidiJournalChapter
        {
            readonly List<KeyValuePair<byte, byte>> noteLog = new List<KeyValuePair<byte, byte>>();
            private byte number;
            private byte velocity;
            public byte GetChapterTypeFlag()
            {
                return 0x08;
            }

            internal RtpMidiJournalChapterNote() { }

            internal RtpMidiJournalChapterNote(byte number, byte velocity)
            {
                this.number = number;
                this.velocity = velocity;
            }

            public byte[] GetChapterData()
            {
                //  0                   1
                //  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |B|     LEN     |  LOW  | HIGH  |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                var noteOnCount = noteLog.Count(x => x.Value != 0);
                var noteOffCount = noteLog.Count(x => x.Value == 0);
                byte low = 0;
                byte high = 0;
                byte lowHigh = 0;
                if (noteOffCount == 0)
                {
                    // The value pairs (LOW = 15, HIGH = 0) and (LOW = 15, HIGH = 1) code an empty NoteOff bitfield structure
                    if (noteOnCount == 128)
                    {
                        // If LEN = 127, LOW = 15, and HIGH = 0, the note list holds 128 note logs
                        lowHigh = 0xf0;
                    }
                    else
                    {
                        // has no OFFBITS octets if LOW = 15 and HIGH = 1
                        lowHigh = 0xf1;
                    }
                }
                else
                {
                    var sortedByNumber = noteLog.OrderBy(x => x.Key);
                    foreach (var note in sortedByNumber)
                    {
                        if (note.Value == 0)
                        {
                            low = (byte)(note.Key / 8);
                            break;
                        }
                    }

                    sortedByNumber = noteLog.OrderByDescending(x => x.Key);
                    foreach (var note in sortedByNumber)
                    {
                        if (note.Value == 0)
                        {
                            high = (byte)(note.Key / 8);
                            break;
                        }
                    }

                    lowHigh = (byte)((low << 4) | high);
                }
                var dataStream = new MemoryStream();
                dataStream.Write(new []
                {
                    (byte)(0x80 | noteOnCount), // B set to 1
                    lowHigh,
                }, 0, 2);

                // Note On
                foreach (var note in noteLog)
                {
                    if (note.Value == 0)
                    {
                        continue;
                    }

                    dataStream.Write(new []
                    {
                        note.Key,
                        note.Value, // The Y bit codes a recommendation to play (Y = 1) or skip (Y = 0)
                    }, 0, 2);
                }
                
                // Note Off bitfields
                var noteLogOrdered = noteLog.OrderBy(x => x.Key);
                byte[] noteOffBitfields = new byte[16];
                foreach (var note in noteLogOrdered)
                {
                    if (note.Value == 0)
                    {
                        noteOffBitfields[note.Key / 8] |= (byte)(1 << (note.Key % 8));
                    }
                }
                for (var i = low; i < high; i++)
                {
                    dataStream.Write(new []{noteOffBitfields[i]}, 0, 1);
                }

                return dataStream.ToArray();
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterNote note)
                {
                    // Remove control from log
                    noteLog.RemoveAll(x => x.Key == note.number);
                    
                    // Add to latest log
                    noteLog.Add(new KeyValuePair<byte, byte>(note.number, note.velocity));
                }
            }
        }

        /// <summary>
        /// Chapter E: Note Command Extras
        /// </summary>
        internal class RtpMidiJournalChapterNoteCommandExtras : IRtpMidiJournalChapter
        {
            public byte GetChapterTypeFlag()
            {
                return 0x04;
            }
            public byte[] GetChapterData()
            {
                throw new NotImplementedException();
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Chapter T: Channel Aftertouch
        /// </summary>
        internal class RtpMidiJournalChapterChannelAftertouch : IRtpMidiJournalChapter
        {
            private byte aftertouch;
            private byte nextAftertouch;
            public byte GetChapterTypeFlag()
            {
                return 0x02;
            }
            internal RtpMidiJournalChapterChannelAftertouch() {}
            internal RtpMidiJournalChapterChannelAftertouch(byte aftertouch)
            {
                nextAftertouch = aftertouch;
            }
            public byte[] GetChapterData()
            {
                if (nextAftertouch == aftertouch)
                {
                    return null;
                }

                aftertouch = nextAftertouch;
                return new[] { aftertouch };
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterChannelAftertouch chapterChannelAftertouch)
                {
                    nextAftertouch = chapterChannelAftertouch.nextAftertouch;
                }
            }
        }

        /// <summary>
        /// Chapter A: Poly Aftertouch
        /// </summary>
        internal class RtpMidiJournalChapterPolyphonicAftertouch : IRtpMidiJournalChapter
        {
            readonly List<KeyValuePair<byte, byte>> noteLog = new List<KeyValuePair<byte, byte>>();
            private readonly byte note;
            private readonly byte pressure;
            internal RtpMidiJournalChapterPolyphonicAftertouch() {}
            internal RtpMidiJournalChapterPolyphonicAftertouch(byte note, byte pressure)
            {
                this.note = note;
                this.pressure = pressure;
            }
            public byte GetChapterTypeFlag()
            {
                return 0x01;
            }
            public byte[] GetChapterData()
            {
                var dataStream = new MemoryStream();
                dataStream.Write(new []
                {
                    (byte)noteLog.Count,
                }, 0, 1);

                // oldest-first ordering rule
                foreach (var polyphonicAftertouch in noteLog)
                {
                    dataStream.Write(new []
                    {
                        polyphonicAftertouch.Key,
                        polyphonicAftertouch.Value,
                    }, 0, 2);
                }

                return dataStream.ToArray();
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterPolyphonicAftertouch chapterPolyphonicAftertouch)
                {
                    // Remove control from log
                    noteLog.RemoveAll(x => x.Key == chapterPolyphonicAftertouch.note);
                    
                    // Add to latest log
                    noteLog.Add(new KeyValuePair<byte, byte>(chapterPolyphonicAftertouch.note, chapterPolyphonicAftertouch.pressure));
                }
            }
        }

        /// <summary>
        /// System Journals
        /// </summary>
        class RtpMidiSystemJournal
        {
            private readonly IRtpMidiJournalChapter[] systemJournals =
            {
                new RtpMidiJournalChapterSimpleSystemCommands(),
                new RtpMidiJournalChapterActiveSenseCommand(),
                // new RtpMidiJournalChapterSequencerStateCommands(),
                // new RtpMidiJournalChapterTimeCodeTapePosition(),
                // new RtpMidiJournalChapterSystemExclusive(),
            };

            internal void RecordJournal(IRtpMidiJournalChapter chapter)
            {
                foreach (var journal in systemJournals)
                {
                    if (journal.GetType() == chapter.GetType())
                    {
                        journal.SetChapterData(chapter);
                    }
                }
            }
            internal bool HasJournal()
            {
                foreach (var journal in systemJournals)
                {
                    if (journal.GetChapterData() != null)
                    {
                        return true;
                    }
                }

                return false;
            }

            internal byte[] GetJournalData()
            {
                var dataStream = new MemoryStream();

                byte journalTypeFlag = 0;
                int journalLength = 0;
                foreach (var journal in systemJournals)
                {
                    var chapterData = journal.GetChapterData();
                    journalTypeFlag |= journal.GetChapterTypeFlag();
                    if (chapterData != null)
                    {
                        journalLength += chapterData.Length;
                    }
                }

                // System Journal Header
                dataStream.Write(
                    new[]
                    {
                        (byte)(journalTypeFlag | ((journalLength >> 8) & 0x3)), // length msb 2bits
                        (byte)(journalLength & 0xff), // length lsb 8bits
                    }, 0, 2);

                // System Journal Chapters
                foreach (var journal in systemJournals)
                {
                    var chapterData = journal.GetChapterData();
                    if (chapterData != null)
                    {
                        dataStream.Write(chapterData, 0, chapterData.Length);
                    }
                }

                return dataStream.ToArray();
            }
        }

        /// <summary>
        /// System Chapter D: Simple System Commands
        /// </summary>
        internal class RtpMidiJournalChapterSimpleSystemCommands : IRtpMidiJournalChapter
        {
            private byte resetCount;
            private byte nextResetCount;
            private byte tuneRequestCount;
            private byte nextTuneRequestCount;
            private byte songSelect;
            private byte nextSongSelect;
            internal RtpMidiJournalChapterSimpleSystemCommands() {}

            public RtpMidiJournalChapterSimpleSystemCommands(MidiType midiType, byte? data = null)
            {
                // |S|B|G|H|J|K|Y|Z|
                if (midiType == MidiType.SystemReset)
                {
                    // Reset (B = 1)
                    nextResetCount = (byte)((resetCount + 1) % 128);
                }
                if (midiType == MidiType.TuneRequest)
                {
                    // Tune Request (G = 1)
                    nextTuneRequestCount = (byte)((tuneRequestCount + 1) % 128);
                }
                if (midiType == MidiType.SongSelect)
                {
                    // Song Select (H = 1)
                    if (data.HasValue)
                    {
                        nextSongSelect = data.Value;
                    }
                }
 
                // TODO
                // undefined System Common 0xF4 (J = 1)
                // undefined System Common 0xF5 (K = 1)
                // undefined System Real-time 0xF9 (Y = 1)
                // undefined System Real-time 0xFD (Z = 1)
            }
            public byte GetChapterTypeFlag()
            {
                return 0x40;
            }
            public byte[] GetChapterData()
            {
                byte chapterFlag = 0;
                if (nextResetCount != resetCount)
                {
                    chapterFlag |= 0x40;
                }
                if (nextTuneRequestCount != tuneRequestCount)
                {
                    chapterFlag |= 0x20;
                }
                if (nextSongSelect != songSelect)
                {
                    chapterFlag |= 0x10;
                }
                if (chapterFlag == 0)
                {
                    return null;
                }

                // Chapter D Header
                var dataStream = new MemoryStream();
                dataStream.Write(new []
                {
                    chapterFlag,
                }, 0, 1);

                // Chapter D Data
                if (nextResetCount != resetCount)
                {
                    dataStream.Write(new []
                        {
                            nextResetCount,
                        }, 0, 1);
                    resetCount = nextResetCount;
                }

                if (nextTuneRequestCount != tuneRequestCount)
                {
                    dataStream.Write(new []
                        {
                            nextTuneRequestCount,
                        }, 0, 1);
                    tuneRequestCount = nextResetCount;
                }

                if (nextSongSelect != songSelect)
                {
                    dataStream.Write(new []
                        {
                            nextSongSelect,
                        }, 0, 1);
                    songSelect = nextSongSelect;
                }

                return dataStream.ToArray();
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterSimpleSystemCommands chapterSimpleSystemCommands)
                {
                    nextResetCount = chapterSimpleSystemCommands.nextResetCount;
                    nextTuneRequestCount = chapterSimpleSystemCommands.nextTuneRequestCount;
                    nextSongSelect = chapterSimpleSystemCommands.nextSongSelect;
                }
            }
        }

        /// <summary>
        /// System Chapter V: Active Sense Command
        /// </summary>
        internal class RtpMidiJournalChapterActiveSenseCommand : IRtpMidiJournalChapter
        {
            private byte count;
            private byte nextCount;
            public byte GetChapterTypeFlag()
            {
                return 0x20;
            }
            internal RtpMidiJournalChapterActiveSenseCommand()
            {
                nextCount++;
                nextCount %= 128;
            }
            public byte[] GetChapterData()
            {
                if (nextCount == count)
                {
                    return null;
                }

                count = nextCount;
                return new[] { (byte)(nextCount & 0x7f) };
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterActiveSenseCommand chapterActiveSenseCommand)
                {
                    nextCount = chapterActiveSenseCommand.nextCount;
                }
            }
        }

        /// <summary>
        /// System Chapter Q: Sequencer State Commands
        /// </summary>
        internal class RtpMidiJournalChapterSequencerStateCommands : IRtpMidiJournalChapter
        {
            private bool running;
            private bool nextRunning;
            private int songPosition;
            private int nextSongPosition;
            private bool isStoppedPosition;
            public RtpMidiJournalChapterSequencerStateCommands(MidiType midiType, int? songPosition = null)
            {
                if (midiType == MidiType.SongPosition && songPosition != null)
                {
                    nextSongPosition = songPosition.Value;
                    if (!nextRunning)
                    {
                        isStoppedPosition = true;
                    }
                }
                if (midiType == MidiType.Clock)
                {
                    if (nextRunning)
                    {
                        nextSongPosition++;
                        isStoppedPosition = false;
                    }
                }
                if (midiType == MidiType.Start)
                {
                    nextRunning = true;
                }
                if (midiType == MidiType.Continue)
                {
                    nextRunning = true;
                }
                if (midiType == MidiType.Stop)
                {
                    nextRunning = false;
                }
            }
            public byte GetChapterTypeFlag()
            {
                return 0x10;
            }
            public byte[] GetChapterData()
            {
                //  0                   1                   2                   3
                //  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |S|N|D|C|T| TOP |            CLOCK              | TIMETOOLS ... |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                if (nextRunning == running && nextSongPosition == songPosition)
                {
                    return null;
                }

                running = nextRunning;
                songPosition = nextSongPosition;
                byte chapterFlag = 0;
                if (running)
                {
                    // N bit: sequencer is currently running
                    chapterFlag |= 0x40;
                }
                if (running && !isStoppedPosition)
                {
                    // D bit: song position is playing recent position(1) or stopped position(0)
                    chapterFlag |= 0x20;
                }

                // C bit: current position of sequence(1)
                chapterFlag |= 0x10;

                // TOP
                chapterFlag |= (byte)((songPosition >> 16) & 0x7);

                // TIMETOOLS: currently zero
                return new byte[] { chapterFlag, (byte)(songPosition >> 8), (byte)(songPosition & 0xff), 0, 0, 0 };
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                if (journal is RtpMidiJournalChapterSequencerStateCommands chapterSequencerStateCommands)
                {
                    nextRunning = chapterSequencerStateCommands.nextRunning;
                    nextSongPosition = chapterSequencerStateCommands.nextSongPosition;
                }
            }
        }

        /// <summary>
        /// System Chapter F: MIDI Time Code Tape Position
        /// </summary>
        internal class RtpMidiJournalChapterTimeCodeTapePosition : IRtpMidiJournalChapter
        {
            public byte GetChapterTypeFlag()
            {
                return 0x08;
            }
            public byte[] GetChapterData()
            {
                //  0                   1                   2                   3
                //  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |S|C|P|Q|D|POINT|  COMPLETE ...                                 |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |     ...       |  PARTIAL  ...                                 |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |     ...       |
                // +-+-+-+-+-+-+-+-+
                throw new NotImplementedException();
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                throw new NotImplementedException();
            }
        }

        internal class RtpMidiJournalChapterSystemExclusive : IRtpMidiJournalChapter
        {
            public byte GetChapterTypeFlag()
            {
                return 0x04;
            }
            public byte[] GetChapterData()
            {
                //  0                   1                   2                   3
                //  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |S|T|C|F|D|L|STA|    TCOUNT     |     COUNT     |  FIRST ...    |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |  DATA ...                                                     |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                throw new NotImplementedException();
            }
            public void SetChapterData(IRtpMidiJournalChapter journal)
            {
                throw new NotImplementedException();
            }
        }

        class RtpMidiChannelJournal
        {
            private readonly IRtpMidiJournalChapter[] channelJournals = {
                new RtpMidiJournalChapterProgramChange(),
                new RtpMidiJournalChapterControlChange(),
                // new RtpMidiJournalChapterParameterSystem(),
                new RtpMidiJournalChapterPitchWheel(),
                new RtpMidiJournalChapterNote(),
                // new RtpMidiJournalChapterNoteCommandExtras(),
                new RtpMidiJournalChapterChannelAftertouch(),
                new RtpMidiJournalChapterPolyphonicAftertouch(),
            };

            internal void RecordJournal(IRtpMidiJournalChapter chapter)
            {
                foreach (var journal in channelJournals)
                {
                    if (journal.GetType() == chapter.GetType())
                    {
                        journal.SetChapterData(chapter);
                    }
                }
            }
            internal bool HasJournal()
            {
                foreach (var journal in channelJournals)
                {
                    if (journal.GetChapterData() != null)
                    {
                        return true;
                    }
                }

                return false;
            }

            internal byte[] GetJournalData(byte channel)
            {
                byte journalTypeFlag = 0;
                int journalLength = 0;
                foreach (var journal in channelJournals)
                {
                    var chapterData = journal.GetChapterData();
                    if (chapterData != null)
                    {
                        journalTypeFlag |= journal.GetChapterTypeFlag();
                        journalLength += chapterData.Length;
                    }
                }

                var dataStream = new MemoryStream();

                // Channel Journal Header
                // |S|CHAN(4bits)|H|LENGTH(10bits)|P|C|M|W|N|E|T|A|
                dataStream.Write(new []
                {
                    (byte)(((channel & 0xf) << 3) | ((journalLength >> 8) & 0x3)), // length msb 2bits
                    (byte)(journalLength & 0xff), // length lsb 8bits
                    journalTypeFlag,
                },0, 3);

                foreach (var journal in channelJournals)
                {
                    var chapterData = journal.GetChapterData();
                    if (chapterData != null)
                    {
                        dataStream.Write(chapterData, 0, chapterData.Length);
                    }
                }

                return dataStream.ToArray();
            }
        }

        private readonly RtpMidiSystemJournal systemJournals = new RtpMidiSystemJournal();
        private readonly RtpMidiChannelJournal[] channelJournals = new RtpMidiChannelJournal[16];

        internal RtpMidiJournal()
        {
            for (var i = 0; i < 16; i++)
            {
                channelJournals[i] = new RtpMidiChannelJournal();
            }
        }
        public void RecordSystemJournal(IRtpMidiJournalChapter chapter)
        {
            systemJournals.RecordJournal(chapter);
        }

        public void RecordChannelJournal(int channel, IRtpMidiJournalChapter chapter)
        {
            channelJournals[channel].RecordJournal(chapter);
        }

        public byte[] GetJournalData()
        {
            // var lostPacketCount = rtp.sequenceNr - participant.receiveSequenceNr - 1;
            // var hasPacketLoss = lostPacketCount > 0;

            var hasChannelJournals = false;
            var totalChannels = 0;
            for (var i = 0; i < 16; i++)
            {
                if (channelJournals[i].HasJournal())
                {
                    hasChannelJournals = true;
                    totalChannels++;
                }
            }

            var hasSystemJournal = systemJournals.HasJournal();

            if (!hasSystemJournal && !hasChannelJournals)
            {
                return null;
            }

            var dataStream = new MemoryStream();

            // Recovery Journal Header
            // TODO detect Single-packet loss(seqnum skipped)
            // |S|Y|A|H|TOTCHAN(4bits)|seqnum(16bits, big endian)|
            var header = totalChannels;
            if (hasSystemJournal)
            {
                header |= 0x40;
            }

            if (hasChannelJournals)
            {
                header |= 0x20;
            }

            dataStream.Write(
                new[]
                {
                    (byte)header,
                    (byte)((sequenceNumber >> 8) & 0xff),
                    (byte)(sequenceNumber & 0xff),
                }
                , 0, 3);

            if (hasSystemJournal)
            {
                // System Journal Chapters
                var systemJournal = systemJournals.GetJournalData();
                dataStream.Write(systemJournal, 0, systemJournal.Length);
            }

            if (hasChannelJournals)
            {
                // Channel Journal Chapters
                for (var i = 0; i < 16; i++)
                {
                    if (channelJournals[i].HasJournal())
                    {
                        var journalData = channelJournals[i].GetJournalData((byte)i);
                        dataStream.Write(journalData, 0, journalData.Length);
                    }
                }
            }

            return dataStream.ToArray();
        }

        /// <summary>
        /// Increments `Checkpoint Packet Seqnum` value
        /// </summary>
        public void IncrementSequenceNumber()
        {
            sequenceNumber++;
        }
    }
}
#endif