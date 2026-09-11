using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEngine;
using jp.kshoji.rtpmidi;

/// <summary>
/// Localhost AppleMIDI loopback for the recovery-journal work that unit tests do not cover:
/// handshake, live MIDI with J=1, loss repaired by a later packet, trailing-loss packets,
/// and disconnect cleanup. Chapter bit layouts stay in the unit tests.
/// </summary>
public sealed class JournalLoopbackSample : MonoBehaviour
{
    const int ListenerPort = 50104;
    const int InitiatorPort = 50114;
    const int Channel = 0;
    const float HandshakeTimeoutSeconds = 4f;
    const float EventTimeoutSeconds = 1.5f;
    const float TrailingTimeoutSeconds = 0.8f;

#if ENABLE_RTP_MIDI_JOURNAL
    const bool JournalEnabled = true;
#else
    const bool JournalEnabled = false;
#endif

    readonly Side listenerSide = new Side("Listener");
    readonly Side initiatorSide = new Side("Initiator");
    readonly Queue<string> pendingLogs = new Queue<string>();
    readonly List<string> logLines = new List<string>();
    readonly List<string> results = new List<string>();

    RtpMidiServer listener;
    RtpMidiServer initiator;
    Vector2 logScroll;
    int shownLogCount;
    bool busy;
    bool failed;
    string status = "停止中";

    void Awake()
    {
        Application.runInBackground = true;
        listenerSide.Log = Log;
        initiatorSide.Log = Log;
    }

    void OnDestroy()
    {
        StopServers();
    }

    void Update()
    {
        lock (pendingLogs)
        {
            while (pendingLogs.Count > 0)
            {
                logLines.Add(pendingLogs.Dequeue());
            }
        }

        if (logLines.Count > 200)
        {
            logLines.RemoveRange(0, logLines.Count - 200);
        }
    }

    void OnGUI()
    {
        var area = new Rect(12, 12, Screen.width - 24, Screen.height - 24);
        GUILayout.BeginArea(area);
        GUILayout.Label("RTP-MIDI Recovery Journal ループバック");
        GUILayout.Label(JournalEnabled
            ? "ENABLE_RTP_MIDI_JOURNAL は有効です。ライブラリ既定値はオフのままです。"
            : "ENABLE_RTP_MIDI_JOURNAL が無効です。回復シナリオは実行できません。");
        GUILayout.Label(status);
        GUILayout.Label(listenerSide.Summary());

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(340));
        DrawButtons();
        GUILayout.Space(8);
        GUILayout.Label("結果");
        for (var i = 0; i < results.Count; i++)
        {
            GUILayout.Label(results[i]);
        }

        GUILayout.EndVertical();

        logScroll = GUILayout.BeginScrollView(logScroll, GUI.skin.box);
        for (var i = 0; i < logLines.Count; i++)
        {
            GUILayout.Label(logLines[i]);
        }

        GUILayout.EndScrollView();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        if (logLines.Count != shownLogCount)
        {
            shownLogCount = logLines.Count;
            logScroll.y = float.MaxValue;
        }
    }

    void DrawButtons()
    {
        GUI.enabled = !busy;
        if (GUILayout.Button("すべて実行"))
        {
            StartCoroutine(RunAll());
        }

        if (GUILayout.Button("接続"))
        {
            StartCoroutine(RunSingle(EnsureConnected, "接続"));
        }

        if (GUILayout.Button("ライブ Note On/Off"))
        {
            StartCoroutine(RunSingle(LiveNoteRoundTrip, "ライブ Note On/Off"));
        }

        GUI.enabled = !busy && JournalEnabled;
        if (GUILayout.Button("NoteOff 損失 → 後続で回復"))
        {
            StartCoroutine(RunSingle(LostNoteOffRecoveredByFollowing, "NoteOff 損失回復"));
        }

        if (GUILayout.Button("Volume 損失 → 後続で回復"))
        {
            StartCoroutine(RunSingle(LostVolumeRecoveredByFollowing, "Volume 損失回復"));
        }

        if (GUILayout.Button("Program / Pitch 損失回復"))
        {
            StartCoroutine(RunSingle(LostProgramAndPitch, "Program / Pitch 損失回復"));
        }

        if (GUILayout.Button("末尾損失 (trailing)"))
        {
            StartCoroutine(RunSingle(TrailingNoteOff, "末尾損失"));
        }

        if (GUILayout.Button("切断時の掃除"))
        {
            StartCoroutine(RunSingle(DisconnectCleanup, "切断時の掃除"));
        }

        GUI.enabled = !busy;
        if (GUILayout.Button("システムメッセージ到達"))
        {
            StartCoroutine(RunSingle(LiveSystemMessages, "システムメッセージ"));
        }

        if (GUILayout.Button("切断して停止"))
        {
            StopServers();
            status = "停止中";
            Log("停止しました");
        }

        GUI.enabled = true;
    }

    IEnumerator RunAll()
    {
        if (busy)
        {
            yield break;
        }

        busy = true;
        failed = false;
        results.Clear();
        Log("--- すべて実行 ---");
        yield return EnsureConnected();
        yield return RunStep("ライブ Note On/Off", LiveNoteRoundTrip);
        if (JournalEnabled)
        {
            yield return RunStep("NoteOff 損失回復", LostNoteOffRecoveredByFollowing);
            yield return RunStep("Volume 損失回復", LostVolumeRecoveredByFollowing);
            yield return RunStep("Program / Pitch 損失回復", LostProgramAndPitch);
            yield return RunStep("システムメッセージ", LiveSystemMessages);
            yield return RunStep("末尾損失", TrailingNoteOff);
            yield return RunStep("切断時の掃除", DisconnectCleanup);
        }
        else
        {
            yield return RunStep("システムメッセージ", LiveSystemMessages);
            Log("回復シナリオは ENABLE_RTP_MIDI_JOURNAL が必要なためスキップしました");
        }

        Log(failed ? "--- 失敗あり ---" : "--- すべて成功 ---");
        status = failed ? "失敗あり" : "すべて成功";
        busy = false;
    }

    IEnumerator RunSingle(Func<IEnumerator> step, string name)
    {
        if (busy)
        {
            yield break;
        }

        busy = true;
        failed = false;
        results.Clear();
        yield return RunStep(name, step);
        status = failed ? "失敗" : "成功";
        busy = false;
    }

    IEnumerator RunStep(string name, Func<IEnumerator> step)
    {
        if (failed)
        {
            results.Add("SKIP " + name);
            yield break;
        }

        Log("-- " + name);
        status = name;
        yield return step();
        results.Add((failed ? "NG  " : "OK  ") + name);
        if (!failed)
        {
            yield return new WaitForSecondsRealtime(0.15f);
        }
    }

    IEnumerator EnsureConnected()
    {
        if (listener != null && initiator != null && initiatorSide.Attached && listenerSide.Attached)
        {
            yield break;
        }

        StopServers();
        listenerSide.ResetConnection();
        initiatorSide.ResetConnection();
        listener = new RtpMidiServer("loopback-listener", ListenerPort, listenerSide, listenerSide);
        initiator = new RtpMidiServer("loopback-initiator", InitiatorPort, initiatorSide, initiatorSide);
        listener.Start();
        initiator.Start();
        initiator.ConnectToListener(new IPEndPoint(IPAddress.Loopback, ListenerPort));
        status = "接続待ち";
        yield return WaitUntil(
            () => listenerSide.Attached && initiatorSide.Attached,
            HandshakeTimeoutSeconds,
            "AppleMIDI ハンドシェイクが完了しませんでした");
        if (!failed)
        {
            Log("接続しました listener=" + listenerSide.PeerDeviceId + " initiator=" + initiatorSide.PeerDeviceId);
        }
    }

    IEnumerator LiveNoteRoundTrip()
    {
        yield return EnsureConnected();
        if (failed)
        {
            yield break;
        }

        yield return ReleaseHeldNotes();
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiNoteOn(PeerId(), Channel, 60, 100));
        yield return WaitUntil(() => listenerSide.NoteDown(Channel, 60), EventTimeoutSeconds, "Note On 60 が届きませんでした");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiNoteOff(PeerId(), Channel, 60, 0));
        yield return WaitUntil(() => !listenerSide.NoteDown(Channel, 60), EventTimeoutSeconds, "Note Off 60 が届きませんでした");
    }

    IEnumerator LostNoteOffRecoveredByFollowing()
    {
        yield return PrepareHeldNote(60, 100);
        if (failed)
        {
            yield break;
        }

        yield return DropOne("Note Off 60");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiNoteOff(PeerId(), Channel, 60, 0));
        yield return WaitUntilDropConsumed("破棄した Note Off が送信バッファから出ていません");
        if (failed)
        {
            yield break;
        }

        if (!listenerSide.NoteDown(Channel, 60))
        {
            Fail("Note Off が破棄されず、そのまま届きました");
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiControlChange(PeerId(), Channel, 1, 7));
        yield return WaitUntil(
            () => !listenerSide.NoteDown(Channel, 60) && listenerSide.ControlSinceArm(1, 7),
            EventTimeoutSeconds,
            "後続パケットのジャーナルで Note Off が回復しませんでした");
    }

    IEnumerator LostVolumeRecoveredByFollowing()
    {
        yield return EnsureConnected();
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiControlChange(PeerId(), Channel, 7, 40));
        yield return WaitUntil(() => listenerSide.ControlSinceArm(7, 40), EventTimeoutSeconds, "Volume 40 が届きませんでした");
        if (failed)
        {
            yield break;
        }

        yield return DropOne("Volume 90");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiControlChange(PeerId(), Channel, 7, 90));
        yield return WaitUntilDropConsumed("破棄した Volume が送信バッファから出ていません");
        if (failed)
        {
            yield break;
        }

        if (listenerSide.ControlIs(7, 90))
        {
            Fail("Volume 90 が破棄されず、そのまま届きました");
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiNoteOn(PeerId(), Channel, 62, 80));
        yield return WaitUntil(
            () => listenerSide.ControlSinceArm(7, 90) && listenerSide.NoteDown(Channel, 62),
            EventTimeoutSeconds,
            "後続パケットのジャーナルで Volume 90 が回復しませんでした");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiNoteOff(PeerId(), Channel, 62, 0));
        yield return WaitUntil(() => !listenerSide.NoteDown(Channel, 62), EventTimeoutSeconds, "回復確認用の Note Off が届きませんでした");
    }

    IEnumerator LostProgramAndPitch()
    {
        yield return EnsureConnected();
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiProgramChange(PeerId(), Channel, 3));
        yield return WaitUntil(() => listenerSide.ProgramSinceArm(3), EventTimeoutSeconds, "Program 3 が届きませんでした");
        if (failed)
        {
            yield break;
        }

        yield return DropOne("Program 12");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiProgramChange(PeerId(), Channel, 12));
        yield return WaitUntilDropConsumed("破棄した Program Change が送信バッファから出ていません");
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiNoteOn(PeerId(), Channel, 64, 70));
        yield return WaitUntil(
            () => listenerSide.ProgramSinceArm(12) && listenerSide.NoteDown(Channel, 64),
            EventTimeoutSeconds,
            "後続パケットのジャーナルで Program 12 が回復しませんでした");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiNoteOff(PeerId(), Channel, 64, 0));
        yield return WaitUntil(() => !listenerSide.NoteDown(Channel, 64), EventTimeoutSeconds, "Program 確認用の Note Off が届きませんでした");
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiPitchWheel(PeerId(), Channel, 8192));
        yield return WaitUntil(() => listenerSide.PitchSinceArm(8192), EventTimeoutSeconds, "Pitch 8192 が届きませんでした");
        if (failed)
        {
            yield break;
        }

        yield return DropOne("Pitch 10000");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiPitchWheel(PeerId(), Channel, 10000));
        yield return WaitUntilDropConsumed("破棄した Pitch Bend が送信バッファから出ていません");
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiNoteOn(PeerId(), Channel, 65, 70));
        yield return WaitUntil(
            () => listenerSide.PitchSinceArm(10000) && listenerSide.NoteDown(Channel, 65),
            EventTimeoutSeconds,
            "後続パケットのジャーナルで Pitch 10000 が回復しませんでした");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiNoteOff(PeerId(), Channel, 65, 0));
        yield return WaitUntil(() => !listenerSide.NoteDown(Channel, 65), EventTimeoutSeconds, "Pitch 確認用の Note Off が届きませんでした");
    }

    IEnumerator LiveSystemMessages()
    {
        yield return EnsureConnected();
        if (failed)
        {
            yield break;
        }

        var sysex = new byte[] { 0xf0, 0x7d, 0x01, 0x02, 0xf7 };
        listenerSide.Arm();
        Send(server => server.SendMidiSystemExclusive(PeerId(), sysex));
        yield return WaitUntil(() => listenerSide.SysExSinceArm(0x7d, 0x01, 0x02), EventTimeoutSeconds, "SysEx が届きませんでした");
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiTimeCodeQuarterFrame(PeerId(), 0x21));
        yield return WaitUntil(() => listenerSide.QuarterFrameSinceArm(0x21), EventTimeoutSeconds, "Quarter Frame が届きませんでした");
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiStart(PeerId()));
        Send(server => server.SendMidiTimingClock(PeerId()));
        yield return WaitUntil(
            () => listenerSide.StartedSinceArm && listenerSide.ClockSinceArm,
            EventTimeoutSeconds,
            "Start または Timing Clock が届きませんでした");
    }

    IEnumerator TrailingNoteOff()
    {
        yield return PrepareHeldNote(67, 90);
        if (failed)
        {
            yield break;
        }

        yield return DropOne("Note Off 67");
        if (failed)
        {
            yield break;
        }

        Send(server => server.SendMidiNoteOff(PeerId(), Channel, 67, 0));
        yield return WaitUntilDropConsumed("破棄した Note Off が送信バッファから出ていません");
        if (failed)
        {
            yield break;
        }

        if (!listenerSide.NoteDown(Channel, 67))
        {
            Fail("末尾損失の Note Off が破棄されず、そのまま届きました");
            yield break;
        }

        Log("追加送信せず、trailing journal を待ちます");
        yield return WaitUntil(
            () => !listenerSide.NoteDown(Channel, 67),
            TrailingTimeoutSeconds,
            "trailing journal で Note Off が回復しませんでした");
    }

    IEnumerator DisconnectCleanup()
    {
        yield return PrepareHeldNote(60, 110);
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Log("ノートを押したまま Initiator を停止します");
        var stopping = initiator;
        initiator = null;
        initiatorSide.ResetConnection();
        stopping.Stop();

        yield return WaitUntil(
            () => listenerSide.CleanupSinceArm && listenerSide.DetachedSinceArm,
            EventTimeoutSeconds,
            "切断時の All Sound Off / Reset All Controllers / All Notes Off または切断通知がありません");
        if (!failed)
        {
            Log("切断掃除を確認しました。押下中ノートは Listener 側で解除されています");
        }
    }

    IEnumerator PrepareHeldNote(int note, int velocity)
    {
        yield return EnsureConnected();
        if (failed)
        {
            yield break;
        }

        yield return ReleaseHeldNotes();
        if (failed)
        {
            yield break;
        }

        listenerSide.Arm();
        Send(server => server.SendMidiNoteOn(PeerId(), Channel, note, velocity));
        yield return WaitUntil(() => listenerSide.NoteDown(Channel, note), EventTimeoutSeconds, "Note On " + note + " が届きませんでした");
    }

    IEnumerator ReleaseHeldNotes()
    {
        var held = listenerSide.HeldNotes();
        if (held.Count == 0)
        {
            yield break;
        }

        for (var i = 0; i < held.Count; i++)
        {
            var note = held[i];
            Send(server => server.SendMidiNoteOff(PeerId(), Channel, note, 0));
        }

        yield return WaitUntil(() => listenerSide.HeldNotes().Count == 0, EventTimeoutSeconds, "押下中のノートを解除できませんでした");
    }

    IEnumerator DropOne(string what)
    {
#if ENABLE_RTP_MIDI_JOURNAL
        initiator.DropNextOutboundMidiPackets(1);
        Log("次の MIDI パケットを破棄します: " + what);
#else
        Fail("ENABLE_RTP_MIDI_JOURNAL が無効です");
#endif
        yield break;
    }

    IEnumerator WaitUntilDropConsumed(string message)
    {
#if ENABLE_RTP_MIDI_JOURNAL
        yield return WaitUntil(() => initiator != null && initiator.OutboundMidiPacketsToDrop == 0, EventTimeoutSeconds, message);
#else
        Fail(message);
        yield break;
#endif
    }

    IEnumerator WaitUntil(Func<bool> ready, float seconds, string message)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            if (ready())
            {
                yield break;
            }

            yield return null;
        }

        Fail(message);
    }

    void Send(Action<RtpMidiServer> send)
    {
        if (failed || initiator == null)
        {
            return;
        }

        send(initiator);
    }

    string PeerId()
    {
        return initiatorSide.PeerDeviceId;
    }

    void Fail(string message)
    {
        if (failed)
        {
            return;
        }

        failed = true;
        Log("NG: " + message);
        status = "失敗: " + message;
    }

    void Log(string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message;
        lock (pendingLogs)
        {
            pendingLogs.Enqueue(line);
        }
    }

    void StopServers()
    {
        if (initiator != null)
        {
            initiator.Stop();
            initiator = null;
        }

        if (listener != null)
        {
            listener.Stop();
            listener = null;
        }

        initiatorSide.ResetConnection();
        listenerSide.ResetConnection();
    }

    sealed class Side : IRtpMidiDeviceConnectionListener, IRtpMidiEventHandler
    {
        readonly object gate = new object();
        readonly bool[,] notes = new bool[16, 128];
        readonly int[] control = new int[128];
        readonly int[] controlStamp = new int[128];
        readonly int[] program = new int[16];
        readonly int[] programStamp = new int[16];
        readonly int[] pitch = new int[16];
        readonly int[] pitchStamp = new int[16];
        readonly string role;

        int clock;
        int armedAt;
        int quarterFrame = -1;
        int quarterFrameStamp;
        int sysExStamp;
        int startStamp;
        int timingClockStamp;
        int cleanupStamp;
        int detachStamp;
        int allSoundOff;
        int resetAllControllers;
        int allNotesOff;
        byte[] lastSysEx;

        public Side(string role)
        {
            this.role = role;
            for (var i = 0; i < control.Length; i++)
            {
                control[i] = -1;
            }

            for (var i = 0; i < 16; i++)
            {
                program[i] = -1;
                pitch[i] = -1;
            }
        }

        public Action<string> Log { get; set; }

        public string PeerDeviceId { get; private set; }

        public bool Attached
        {
            get { return PeerDeviceId != null; }
        }

        public void Arm()
        {
            lock (gate)
            {
                armedAt = clock;
            }
        }

        public void ResetConnection()
        {
            lock (gate)
            {
                PeerDeviceId = null;
                detachStamp = 0;
                Array.Clear(notes, 0, notes.Length);
            }
        }

        public bool NoteDown(int channel, int note)
        {
            lock (gate)
            {
                return notes[channel, note];
            }
        }

        public List<int> HeldNotes()
        {
            var held = new List<int>();
            lock (gate)
            {
                for (var note = 0; note < 128; note++)
                {
                    if (notes[Channel, note])
                    {
                        held.Add(note);
                    }
                }
            }

            return held;
        }

        public bool ControlIs(int number, int value)
        {
            lock (gate)
            {
                return control[number] == value;
            }
        }

        public bool ControlSinceArm(int number, int value)
        {
            lock (gate)
            {
                return controlStamp[number] > armedAt && control[number] == value;
            }
        }

        public bool ProgramSinceArm(int value)
        {
            lock (gate)
            {
                return programStamp[Channel] > armedAt && program[Channel] == value;
            }
        }

        public bool PitchSinceArm(int value)
        {
            lock (gate)
            {
                return pitchStamp[Channel] > armedAt && pitch[Channel] == value;
            }
        }

        public bool QuarterFrameSinceArm(int value)
        {
            lock (gate)
            {
                return quarterFrameStamp > armedAt && quarterFrame == value;
            }
        }

        public bool StartedSinceArm
        {
            get
            {
                lock (gate)
                {
                    return startStamp > armedAt;
                }
            }
        }

        public bool ClockSinceArm
        {
            get
            {
                lock (gate)
                {
                    return timingClockStamp > armedAt;
                }
            }
        }

        public bool SysExSinceArm(byte a, byte b, byte c)
        {
            lock (gate)
            {
                return sysExStamp > armedAt && lastSysEx != null && IndexOf(lastSysEx, a, b, c) >= 0;
            }
        }

        public bool CleanupSinceArm
        {
            get
            {
                lock (gate)
                {
                    return cleanupStamp > armedAt && allSoundOff >= 16 && resetAllControllers >= 16 && allNotesOff >= 16;
                }
            }
        }

        public bool DetachedSinceArm
        {
            get
            {
                lock (gate)
                {
                    return detachStamp > armedAt;
                }
            }
        }

        public string Summary()
        {
            lock (gate)
            {
                var held = new StringBuilder();
                for (var note = 0; note < 128; note++)
                {
                    if (!notes[Channel, note])
                    {
                        continue;
                    }

                    if (held.Length > 0)
                    {
                        held.Append(',');
                    }

                    held.Append(note);
                }

                return role + " notes=[" + held + "] vol=" + control[7] + " prog=" + program[Channel] + " pitch=" + pitch[Channel];
            }
        }

        public void OnRtpMidiDeviceAttached(string deviceId)
        {
            lock (gate)
            {
                PeerDeviceId = deviceId;
            }

            Log(role + " attached " + deviceId);
        }

        public void OnRtpMidiDeviceDetached(string deviceId)
        {
            lock (gate)
            {
                detachStamp = ++clock;
                PeerDeviceId = null;
            }

            Log(role + " detached " + deviceId);
        }

        public void OnMidiNoteOn(string deviceId, int channel, int note, int velocity)
        {
            lock (gate)
            {
                notes[channel, note] = velocity > 0;
                clock++;
            }

            Log(role + " NoteOn ch=" + channel + " n=" + note + " v=" + velocity);
        }

        public void OnMidiNoteOff(string deviceId, int channel, int note, int velocity)
        {
            lock (gate)
            {
                notes[channel, note] = false;
                clock++;
            }

            Log(role + " NoteOff ch=" + channel + " n=" + note + " v=" + velocity);
        }

        public void OnMidiControlChange(string deviceId, int channel, int function, int value)
        {
            lock (gate)
            {
                var stamp = ++clock;
                control[function] = value;
                controlStamp[function] = stamp;
                if (function == RtpMidiIndefiniteState.AllSoundOff)
                {
                    allSoundOff++;
                    ClearChannel(channel);
                    cleanupStamp = stamp;
                }
                else if (function == RtpMidiIndefiniteState.ResetAllControllers)
                {
                    resetAllControllers++;
                    cleanupStamp = stamp;
                }
                else if (function == RtpMidiIndefiniteState.AllNotesOff)
                {
                    allNotesOff++;
                    ClearChannel(channel);
                    cleanupStamp = stamp;
                }
            }

            Log(role + " CC ch=" + channel + " n=" + function + " v=" + value);
        }

        public void OnMidiProgramChange(string deviceId, int channel, int programNumber)
        {
            lock (gate)
            {
                program[channel] = programNumber;
                programStamp[channel] = ++clock;
            }

            Log(role + " Program ch=" + channel + " p=" + programNumber);
        }

        public void OnMidiPitchWheel(string deviceId, int channel, int amount)
        {
            lock (gate)
            {
                pitch[channel] = amount;
                pitchStamp[channel] = ++clock;
            }

            Log(role + " Pitch ch=" + channel + " a=" + amount);
        }

        public void OnMidiSystemExclusive(string deviceId, byte[] systemExclusive)
        {
            lock (gate)
            {
                lastSysEx = systemExclusive;
                sysExStamp = ++clock;
            }

            Log(role + " SysEx " + (systemExclusive == null ? 0 : systemExclusive.Length) + " bytes");
        }

        public void OnMidiTimeCodeQuarterFrame(string deviceId, int timing)
        {
            lock (gate)
            {
                quarterFrame = timing;
                quarterFrameStamp = ++clock;
            }

            Log(role + " QuarterFrame " + timing);
        }

        public void OnMidiStart(string deviceId)
        {
            lock (gate)
            {
                startStamp = ++clock;
            }

            Log(role + " Start");
        }

        public void OnMidiTimingClock(string deviceId)
        {
            lock (gate)
            {
                timingClockStamp = ++clock;
            }

            Log(role + " Clock");
        }

        public void OnMidiPolyphonicAftertouch(string deviceId, int channel, int note, int pressure) { }
        public void OnMidiChannelAftertouch(string deviceId, int channel, int pressure) { }
        public void OnMidiSongSelect(string deviceId, int song) { }
        public void OnMidiSongPositionPointer(string deviceId, int position) { }
        public void OnMidiTuneRequest(string deviceId) { }
        public void OnMidiContinue(string deviceId) { }
        public void OnMidiStop(string deviceId) { }
        public void OnMidiActiveSensing(string deviceId) { }
        public void OnMidiReset(string deviceId) { }

        void ClearChannel(int channel)
        {
            for (var note = 0; note < 128; note++)
            {
                notes[channel, note] = false;
            }
        }

        static int IndexOf(byte[] data, byte a, byte b, byte c)
        {
            for (var i = 0; i + 2 < data.Length; i++)
            {
                if (data[i] == a && data[i + 1] == b && data[i + 2] == c)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
