# RTP-MIDI Recovery Journal 実装計画

正本は [RFC 6295](https://datatracker.ietf.org/doc/html/rfc6295)（RFC 4695 を廃止）である。ジャーナリングを「使う」と決めたストリームは、同 RFC の Recovery Journal 既定セマンティクスと recovery journal mandate を満たす。アルゴリズムの非規範ガイドは [RFC 4696](https://datatracker.ietf.org/doc/html/rfc4696) を参照する。セッション制御は AppleMIDI（`IN` / `OK` / `BY` / `CK` / `RS`）であり、SDP の `j_update` / `ch_never` などによる緩和は使わない。

試作は `#if ENABLE_RTP_MIDI_JOURNAL` 配下にある。公開ビルドは現状オフのまま、本計画の完了をもってオンにする。

対象ファイル:

- `Runtime/RtpMidiJournal.cs` … 送信側の履歴とエンコード
- `Runtime/RtpMidiParser.cs` … Recovery Journal 節のデコード
- `Runtime/RtpMidiSession.cs` … 送受信、`RS`、セッション寿命
- `Runtime/RtpMidiServer.cs` … アプリからの MIDI 送信
- `Runtime/RtpMidiProtocol.cs` … AppleMIDI `RS`

---

## 1. 目的

**Recovery journal mandate**（RFC 6295 §4）: 不可信頼送路上の RTP MIDI からレンダリングした演奏に、**不定アーティファクトを残してはならない**。

- 不定: 欠落が演奏を長く壊すもの。Lost NoteOff、Channel Volume の欠落、Sustain Off の欠落など
- 一時的: その場限りの欠け。Lost NoteOn で音が出ない、など。mandate の必須修復対象ではないが、チャプタの MUST 包含規則は一時的軽減用の章（E など）にも存在する。包含規則が MUST なら実装する

本計画の完了条件は「よく使うチャプタだけ動く」ではなく、**このライブラリが送信しうる MIDI について、既定 Recovery Journal の MUST をすべて実装すること**である。API（`RtpMidiServer.SendMidi*`）はノート、CC、Program、Pitch Bend、Aftertouch、SysEx、MTC、ソング／クロック、Active Sense、Reset を送れる。対応チャプタを後回しにしない。

使わないもの（実装しない）:

| 項目 | 理由 |
|---|---|
| enhanced Chapter C（H=1） | 既定は H=0。SDP でだけ有効化される |
| open-loop / anchor 送信ポリシー | 既定は closed-loop。SDP `j_update` が無い |
| 代替 journal 形式、mpeg4-generic | 本ライブラリの輸送は native RTP MIDI over UDP |
| 送信 MIDI リストへの phantom 挿入 | 修復はローカルレンダラへ出す。MIDI 節の P ビットは常に 0 |

切断・再接続は journal の別プロトコルではない。RFC が既に要求する **セッション入場（最初のパケットをロス末尾として扱う）** と **退場（不定アーティファクトで終わらせない）** として実装する。

---

## 2. 用語（Appendix A.1）

パケット I が今送る／今届いたパケット、C が Checkpoint Packet Seqnum のパケット。シーケンス計算は modulo 2^16。拡張シーケンスは送信側で 32 bit 相当を持つ。

- **Checkpoint history**: パケット C から I−1 までの MIDI Command Section の連結。**I の MIDI 節は含まない**。C = I なら空
- **Session history**: セッション先頭から I−1 まで。先頭パケットでは空
- **Reset State コマンド**: System Reset (`0xFF`)、および RFC が列挙する GM/DLS 系 SysEx
- **Active**: session history 上で、より新しい Reset State より前にないコマンド
- **N-active**: より新しい CC 120 / 123–127、または Reset State より前にないコマンド
- **C-active**: より新しい CC 121（Reset All Controllers）、または Reset State より前にないコマンド
- **Oldest-first**: リストは session history で古いコマンドが先、新しいコマンドが末尾
- **Finished / unfinished**: 分割 SysEx が複数パケットにまたがるとき。Chapter X で扱う

「checkpoint より後のスナップショット」ではなく、**C..I−1 のコマンド集合に対する包含規則**でチャプタを出す。実装がスナップショットを持っても、出す／出さないの判定は上記定義に一致させる。

---

## 3. ストリーム全体の MUST

### 3.1 パケット

- ジャーナリングを使う UDP ストリームでは、**すべてのペイロードに journal 節がある**（MIDI ヘッダ J=1）。変化が無くても **空 journal（Y=0 かつ A=0 の 3 オクテット）** を付ける。J=0 は不可
- J=1 と journal 節の有無は一致する
- MIDI 節 LEN=0 のパケット（journal のみ）は合法であり、後述の trailing loss 対策に使う
- LENGTH はその構造のオクテット数で、**自身のヘッダを含む**。受信は内部形式ではなく LENGTH でスキップする
- R ビットは送信 0、受信無視
- チャネル journal は **CHAN 昇順**、高々 1 チャネル 1 個。TOTCHAN+1 個
- チャプタは TOC ビットの出現順
- enhanced Chapter C を使わないので、トップレベル H とチャネル H は **全パケット 0**

### 3.2 S ビット（省略エンコードではない）

原則 **S=1**。要素がパケット I−1 の MIDI コマンドに関するデータを含むとき、その要素の S は **0**。S=0 の要素を含む上位要素（チャネル journal、システム journal、トップレベル）も S=0。

Chapter N の **B ビット**は NoteOff ビットフィールド用の同じ規則: 直前パケットにそのチャネルの NoteOff があれば B=0（そのとき上位 S も 0）。

S=1 を「パースせず捨てる」ことは禁止。受信は常に LENGTH で構造を消費し、適用判定は §4 に従う。

### 3.3 送信ポリシー（closed-loop, Appendix C.2.2.2）

既定ポリシーを使う。フィードバックは RTCP RR の代替として AppleMIDI `RS` を使う（RFC は別メカニズム合意を認めている）。

受信者 k の M(k) は、その相手が見た最高 RTP シーケンス（送信側のラップ回数で正規化した拡張シーケンス）。`RS` がまだ無いときは、その相手を認識した時点の定義に従う（ユニキャストでは先頭パケット番号）。

新しいパケットを出すとき、checkpoint の拡張番号 N は **すべての既知受信者 k について M(k) ≥ N−1**。N を送信のたびに +1 してはならない。

`RS` が途絶えると history が伸びる。肥大したらその participant をタイムアウト切断する（既存の同期タイムアウトに載せる）。

新しい受信者（再接続を含む）が不定アーティファクトを受けるなら、送信側は **session history 相当の状態が journal に載る** ように C を十分古くする（または同等の complete journal）。最初の `RS` がそれを覆うまで続ける。

AppleMIDI `RS` は 16 bit シーケンス（実装は上位 16 bit、下位は 0）しか運ばない。ラップ回数は送信側が拡張シーケンスとして持つ。`short` の符号付き比較で欠番や checkpoint を計算しない。

### 3.4 受信ポリシー（§4）

シーケンスの切れ目を検出する。

- **ロス**（RFC 3550）: 不定アーティファクトをすべて修復する。修復は、ロスを終わらせるパケットの journal と、受信側が既に知っている履歴の差から MIDI を実行して行う
- **順序入れ替わり**: 不定アーティファクトを導入しない。遅延パケットを待つか、そのパケットを無視するのが安全
- 欠番が無いとき、journal を適用してはならない（二重実行）
- **ストリームで最初に届いたパケットは、ロスを終わらせるパケットとして扱う**（journal を適用する）
- 受信 MIDI Command Section は、ロスの有無に関係なく再生する。journal は欠落分の穴埋めであり、当パケットの MIDI 節の代替ではない
- Checkpoint Packet Seqnum が欠番区間を覆うか検証する。覆う条件: C ≤（それまで受信した最高シーケンス + 1）（mod 2^16）。覆わない深いロスでは journal だけでは足りない。Note Off / CC 120 / 123 / 121 などで不定状態を切る
- **セッション退場時**、レンダリングが不定アーティファクトで終わってはならない
- 修復コマンドの実行時刻は欠落パケットの元タイムスタンプではない。届いた時点（またはプレイアウト時点）で適用する。本ライブラリにプレイアウトバッファは無いので、検出時点で適用する

### 3.5 Trailing loss

ロスを終わらせるパケットが無ければ受信側は修復できない。無音が続き、かつ checkpoint history にまだ N-active ノートや未 ACK の状態があるあいだは、**MIDI 節 LEN=0 の journal 付きパケット**をデータポートへ出す。既存のセッションスレッドに載せる。`RS` が追いつくか、切断したら止める。

切断時は受信側ローカルで All Notes Off / All Sound Off / Reset All Controllers をイベントハンドラへ出す。可能なら切断前に相手へも送る。

---

## 4. 送信・受信の役割

```
アプリ SendMidi*
  └─ その宛先の session history に Record（送信 MIDI のみ）
  └─ outMidiBuffer → RTP
        ├─ MIDI Command Section（ソースコマンド。P=0）
        └─ J=1 Recovery Journal（C..I−1 に対するチャプタ）

着信 RTP
  ├─ 最初のパケットまたはロス末尾 → journal をレンダラへ適用
  ├─ 順序入れ替わり → 適用しない（無視または待機）
  ├─ MIDI Command Section は常に再生
  └─ 周期的に RS（受信した最高シーケンス）を返す

着信 RS
  └─ その送信先の M(k) を更新し、次回パケットの C を選ぶ
```

- `SendJournal` は宛先ごとに、自分が送った MIDI の session history からチャプタを作る
- 着信 MIDI を送信 journal に入れない
- 着信 journal を送信 journal に入れない。適用先は `IRtpMidiEventHandler`
- `HasJournal` / エンコードは履歴を消費しない。エンコードは読み取り専用

記録の入口は `SendMidiRaw` / `Write`。`ReceivedMidi(participant, …)` から `Record*` を削除する。

---

## 5. チャプタ包含（既定セマンティクスの MUST）

条件を満たすチャプタは必ず出し、チャネル／システム journal も出す。条件を満たさないチャプタはフラグもペイロードも出さない。空のチャネル journal は出さない。

### 5.1 チャネル

| 章 | MUST で出す条件 | 実装上の要点 |
|---|---|---|
| **N** | checkpoint history に N-active な NoteOn または NoteOff | vel 0 の NoteOn は NoteOff。オンはノートログ（oldest-first、同一番号は 1 つ、VELOCITY≠0、Y は再生ヒント）、オフは OFFBITS。両方に同じ番号を出さない。OFFBITS は各オクテットの **MSB が低いノート**（最初の MSB = 8×LOW）。LOW≤HIGH なら HIGH−LOW+1 オクテット。(15,0)/(15,1) は空ビットフィールド。LEN=127 かつ LOW=15 HIGH=0 はノートログ 128。B ビットは §3.2。Hold Pedal との前後は journal に乗らない。不明ならペダルを切る側（SHOULD） |
| **C** | ログが 1 つ以上必要 | 既定はコントローラ番号ごとに **最新の active CC 1 コマンド**。oldest-first。ログは S+NUMBER+A+VALUE/ALT。連続量は **value tool（A=0）**。スイッチ（CC64 など）は lost Off が不定になるため **toggle tool（A=1,T=0）** を使う（mandate）。All Notes Off 系など値を無視する番号は **count tool（A=1,T=1）**。複数 tool を出すなら count → value → toggle。14-bit MSB/LSB の省略例外、Bank が P に載るときの C 省略、124/125 と 126/127 の排他、CC121 後に RP-015 でリセットされる番号の省略、**CC#7 は省略禁止**。**CC 6/38/96–101 のパラメータトランザクションは C に出さず M へ** |
| **M** | パラメータログが必要、または checkpoint 末尾が RPN/NRPN MSB、または null パラメータを完成した LSB | 送信側は CC 6/38/96–101 の役割をストリームから推定する。C と二重に出さない |
| **W** | checkpoint history に C-active Pitch Bend | 最新 active の 14 bit。CC121 後は C-active ではないので出さない |
| **P** | checkpoint history に active Program Change | PROGRAM は session history の最新 active。Bank MSB がその Program より前にあれば B=1。その間の Bank LSB。その間に CC121 があれば X=1 |
| **E** | Appendix A.7.2 | NoteOff リリースベロシティが 64 以外、または同一ノートの重ね NoteOn（参照カウント）。V=1 がベロシティ、V=0 がカウント。N を補完する。ライブラリは NoteOff velocity を送れるので必須 |
| **T** | checkpoint history に N-active かつ C-active な Channel Aftertouch | 1 オクテット |
| **A** | checkpoint history に C-active Poly Aftertouch | ノート番号ごとに 1 ログ、oldest-first。LEN は個数−1 |

チャネル TOC 順: P C M W N E T A。

### 5.2 システム

システム TOC 順: D V Q F X。いずれかが必要ならシステム journal を出す。

| 章 | MUST で出す条件 | 要点 |
|---|---|---|
| **D** | checkpoint history に active な Reset / Tune Request / Song Select / 未定義 F4,F5,F9,FD | Reset/Tune は COUNT mod 128。Song Select は最新 VALUE。未定義コマンドは API が送らなくても、受信と将来の raw のためにデコードし、送るならエンコードする |
| **V** | active Active Sense | COUNT mod 128 |
| **Q** | active な SPP / Clock / Start / Continue / Stop が、章のビット内容を変えるとき | N/D/C/T と CLOCK。Start/Continue が最近なら N=1、Stop または未出現なら N=0 |
| **F** | active な Quarter Frame (`0xF1`) または finished な MTC Full Frame SysEx | それ以外では **MUST NOT** 出す。`SendMidiTimeCodeQuarterFrame` があるので実装する |
| **X** | Appendix B.5.2 がログを要求 | 分割 SysEx の finished/unfinished、キャンセル。システム journal の LENGTH からログ個数を知る（ログに全体ヘッダは無い）。oldest-first。GM/DLS の Reset State SysEx は session history の active 定義にも効く |

---

## 6. 現行試作との差分（破棄する前提）

- 着信 `ReceivedMidi` で Record している → 送信経路へ移す
- `lostPacketCount > 0` のときだけ J=1 → **常に J=1**
- `IncrementSequenceNumber()` → checkpoint は M(k) から選ぶ
- `HasJournal()` が `GetChapterData()` で履歴を消す → 禁止
- S=1 を未実装としてヘッダだけ消費 → 禁止
- 欠番なしでもチャネル journal をイベント化する → 禁止
- 最初のパケットを通常再生だけしている → ロス末尾として journal 適用
- チャネル順・LENGTH のヘッダ込み・OFFBITS のビット順が RFC と不一致のまま
- Chapter M/E/F/X が `NotImplementedException` または読み飛ばしだけ → 送信包含規則を実装する（受信の LENGTH スキップは残しつつ中身も適用する）

`MaxBufferSize`（64）は MIDI 節用のままにし、journal は別見積もりとする。UDP は経路 MTU を超えない（SHOULD、Ethernet ならおおよそ 1500 未満）。溢れるときは MIDI 節を先に出し、続くパケットに journal を載せる。journal 自体が MTU を超えるまで history が伸びたら、その相手を切断する。

---

## 7. 段階

各段階の完了後、その範囲で RFC の MUST に対して退行しない。後半のチャプタが未配線のあいだ、**対応 MIDI を送ると包含違反になる**。そのため段階のあいだは、未実装チャプタに対応する `SendMidi*` をジャーナリング・オン時に拒否するか、フラグをオフのまま開発する。**公開でフラグをオンにするのは全段階完了後**。

### Phase 0 — 基盤

経路とパケット契約。チャプタ中身は空 journal でよい。

1. フラグを csproj 等で明示切替（既定オフ）
2. 送信専用履歴。Record は履歴更新のみ。Encode は読み取り専用
3. Record を送信経路へ。受信 Record を削除
4. ジャーナリング・オンなら **全 RTP-MIDI ペイロードで J=1**。無内容なら空 journal
5. LENGTH / TOTCHAN / チャネル昇順 / H=0 / R=0
6. S ビット規則（I−1 を含む要素だけ 0、上位へ伝播）。スキップ最適化はしない
7. 受信は LENGTH で必ず消費する
8. MIDI 節と journal 長を分けて見積もる
9. シーケンスを modulo 2^16 と拡張 32 bit で扱う土台

完了条件: フラグオンでビルドできる。空 journal 付きパケットが送れる。相手の非空 journal を長さだけで安全に読み飛ばせる。Encode の有無確認で履歴が変わらない。

### Phase 1 — 受信適用とセッション出入り

チャプタが空でも mandate の受信側 MUST を先に固定する。

1. 最初のパケットをロス末尾として扱う
2. ロスと順序入れ替わりを区別する。入れ替わりでは journal を適用しない
3. 欠番なしでは journal を適用しない
4. checkpoint が欠番を覆わないときは All Notes Off / All Sound Off / RAC で不定状態を切る
5. 切断・タイムアウト・`BY` でローカルに不定状態を残さない
6. trailing loss 用の LEN=0 + journal パケット
7. `RS` を周期送信（既存 `ManageReceiverFeedback` を、拡張シーケンス前提で直す）

完了条件: 初回パケットと切断でハングノートが残らない。順序入れ替わりで二重 NoteOn が起きないテストがある。

### Phase 2 — closed-loop checkpoint

1. C は M(k) から選ぶ。`RS.SequenceNr` で M(k) を更新。符号付き `short` 比較をやめる
2. 新規／再接続の相手には、不定が残らない深さの journal（complete）を、最初の `RS` まで付ける
3. `RS` 途絶で history が肥大したら切断
4. 空 history は C=I の空 journal

完了条件: `RS` が追いつくと journal が縮小する。追いつかないあいだは必要なチャプタが残る。ラップ付近で C と欠番が壊れない。

### Phase 3 — Chapter N と E

不定の中心（鳴りっぱなし）と、包含 MUST の E。

- N のエンコード／デコードを §5.1 どおり
- All Notes Off / All Sound Off / Reset で N-active を落とす
- E: NoteOff vel≠64、重ね NoteOn の参照カウント
- 受信適用は Phase 1 の条件のときだけ

完了条件: 欠番で NoteOff が落ちても次パケットで音が止まる。vel≠64 の NoteOff が E で戻る。同一ノートがログと OFFBITS の両方に出ない。OFFBITS ビット順が RFC と一致する。

### Phase 4 — Chapter C と M

コントローラの不定アーティファクト。C と M は同時に完成させる。

- C: value / toggle / count、oldest-first、排他対、CC121 と Volume、14-bit 例外
- パラメータ系 CC は M。C に出さない
- M: 進行中トランザクション、PENDING、null パラメータ、包含 MUST
- Reset State / CC121 で C-active を更新

完了条件: Volume 欠落が戻る。CC64 の on→off→on で off 欠落を toggle で検出できる。RPN 操作が C に漏れない。

### Phase 5 — Chapter P / W / T / A

包含 MUST どおり。P の Bank と X（CC121）。W/T は C-active（と T は N-active）。A は oldest-first。

Bank を P に載せたコマンドは C から省略してよい（MAY）。省略するならテストで固定する。

完了条件: 各メッセージが checkpoint history にあるとき必ず章が出る。無いとき章が出ない。CC121 後に古い Pitch Bend / Aftertouch で修復しない。

### Phase 6 — システム D / V / Q / F / X

API が送るシステム／SysEx／MTC の包含 MUST。Reset State SysEx は active 定義に反映する。

分割 SysEx は既存の MIDI 節セグメントと X の unfinished 定義を一致させる。F は F1 または Full Frame があるときだけ。

完了条件: Reset / Clock+Start / Active Sense / Quarter Frame / 通常 SysEx について、包含規則と欠番適用のテストがある。F の MUST NOT もテストする。

### Phase 7 — 公開

- フラグ既定オン、またはオンでリリース
- README から「journaling 非対応」を削除し、RFC 6295 Recovery Journal（既定セマンティクス、closed-loop、AppleMIDI `RS`）と書く
- enhanced Chapter C と SDP 緩和は非対応と明記する
- CHANGELOG を更新する

---

## 8. テスト

Phase 0 の終わりまでにテストプロジェクトを追加する。ネットワーク無しで Record/Encode と Decode をバイト列検証する。

必須ケース:

- 空 journal 3 オクテット、J=1
- C=I で history 空
- C..I−1 にしか載らない（I の MIDI が journal に出ない）
- チャネル昇順、LENGTH がヘッダ込み、H=0
- S=0 の伝播（I−1 の NoteOff で N の B=0 と上位 S=0）
- N: オンのみ、オフのみ、オン→オフ、vel 0、OFFBITS の MSB=低いノート、128 ノート
- E: vel≠64、重ね NoteOn
- C: 上書き、124/125、Volume 省略禁止、CC64 toggle、6/38/96–101 が C に無い
- M: 部分 RPN と null
- P/W/T/A の包含と CC121 後の非包含
- D/V/Q/F/X の包含。F の MUST NOT
- 初回パケット適用、欠番なし非適用、順序入れ替わり非適用、被覆失敗時のフォールバック
- 16 bit ラップと `RS` による C の前進
- Reset State / CC 120 / 123–127 / 121 で N-active / C-active / active が落ちる

結合: ループバックでパケット間引き。切断中のノートがローカルで止まること。trailing の空パケットで末尾 NoteOff ロスが直ること。

---

## 9. 進め方

```
Phase 0  基盤（常時 J=1、LENGTH、S、履歴モデル）
   │
Phase 1  受信 MUST（初回適用、ロス/順序、退場、trailing）
   │
Phase 2  closed-loop（RS、C の選択、再接続 complete）
   │
Phase 3  Chapter N / E
   │
Phase 4  Chapter C / M
   │
Phase 5  Chapter P / W / T / A
   │
Phase 6  Chapter D / V / Q / F / X
   │
Phase 7  公開（フラグオン、README）
```

Phase 3 より前にフラグを既定オンにしない。未実装チャプタの MIDI をオンのまま送らない。

実装時のビット配置・包含の細部は、本計画より RFC 6295 の該当節（§3–5、Appendix A/B、C.2.2.2）を優先する。矛盾があれば RFC に合わせて本ファイルを直す。
