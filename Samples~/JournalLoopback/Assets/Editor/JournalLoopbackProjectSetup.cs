using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
static class JournalLoopbackProjectSetup
{
    const string Symbol = "ENABLE_RTP_MIDI_JOURNAL";
    const string ScenePath = "Assets/Scenes/JournalLoopback.unity";
    const string OpenedPref = "RtpMidi.JournalLoopback.OpenedScene";

    static JournalLoopbackProjectSetup()
    {
        EnsureDefine();
        EditorApplication.delayCall += OpenSampleOnce;
    }

    [MenuItem("RTP-MIDI/Open Journal Loopback Sample")]
    static void OpenSample()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("Play 中はシーンを開けません。Play を止めてから開いてください。");
            return;
        }

        EnsureDefine();
        EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("RTP-MIDI/Open Journal Loopback Sample", true)]
    static bool OpenSampleValidate()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    static void EnsureDefine()
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        var current = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
        if (HasSymbol(current))
        {
            return;
        }

        var next = string.IsNullOrEmpty(current) ? Symbol : current + ";" + Symbol;
        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, next);
        Debug.Log(Symbol + " を Player Settings に追加しました。このプロジェクトだけで有効です。");
    }

    static void OpenSampleOnce()
    {
        if (EditorPrefs.GetBool(OpenedPref, false) || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (!System.IO.File.Exists(ScenePath))
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        EditorPrefs.SetBool(OpenedPref, true);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath);
        }
    }

    static bool HasSymbol(string defines)
    {
        if (string.IsNullOrEmpty(defines))
        {
            return false;
        }

        var parts = defines.Split(';');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Trim() == Symbol)
            {
                return true;
            }
        }

        return false;
    }
}
