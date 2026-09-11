# Journal Loopback

Recovery Journal の結合確認用 Unity プロジェクトです。単体テストがカバーするチャプタのバイト列はここでは見ません。localhost の AppleMIDI セッション上で、次だけを確認します。

- ハンドシェイク
- J=1 のまま Note On/Off、SysEx、Quarter Frame、Start、Timing Clock が届く
- Note Off / Volume / Program / Pitch Bend を1パケット落としたあと、後続パケットのジャーナルで回復する
- 後続を送らず、trailing journal だけで Note Off が回復する
- ノートを押したまま切断すると、Listener に All Sound Off、Reset All Controllers、All Notes Off が出て切断通知が来る

`ENABLE_RTP_MIDI_JOURNAL` はこのプロジェクトの Player Settings だけで有効です。ライブラリの既定値はオフのままです。

## 実行

1. Unity 2019.4 以降（2021.3 を推奨）で `Samples~/JournalLoopback` を開く。
2. 初回は `Assets/Scenes/JournalLoopback.unity` が開きます。違う場合はメニュー `RTP-MIDI/Open Journal Loopback Sample`。
3. Play して **すべて実行**。

Listener の制御ポートは 50104、Initiator は 50114 です。使用中だと接続に失敗します。
