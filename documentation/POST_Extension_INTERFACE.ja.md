# GATE / LED POST notifications Extension インターフェース仕様書（日本語）

バージョン: 1.1  
方向: FPVTrackside（送信側）→ Extension（受信側）、一方向  
対象読者: Extension またはテストクライアントを実装する開発者

本書は単独で完結します。この1ファイルだけを参照して動作するテストクライアントを実装できます。

---

## 1. Extension有効条件と後方互換性

本仕様書に記載された通信は、FPVTrackside の **`ExtensionMode` 設定が `true`** のときに**のみ**発生します。設定は *Application Profile Settings* の **「Gate / LED POST notifications」** カテゴリにあり、デフォルトは `false`、変更後は再起動が必要です。

`ExtensionMode = false` のとき:
- 本書に記載された通信は一切発生しません
- 既存の `GATE / LED POST notifications` の動作（`NotificationEnabled`, `NotificationURL`, `NotificationSerialPort` で制御）は完全に保持されます（既存のバグも含む）

`ExtensionMode = true` のとき:
- 別コンポーネント `ExtensionNotifier` が起動し、本書のイベントを送出します
- レガシー `RemoteNotifier` は `NotificationEnabled = true` であっても**抑止されます**。ExtensionMode は既存のデータストリームを置き換えるため、両者が同じ URL/COM ポートに同時送信することはありません。

---

## 2. 通信方式

### 2.1 HTTP

| 項目 | 値 |
|---|---|
| メソッド | `PUT` |
| 宛先 URL | プロファイル設定 `NotificationURL` の値（例: `http://127.0.0.1:8765/`） |
| ヘッダ | `Content-Type: application/json; charset=utf-8` |
| ボディ | 1リクエストあたり JSON オブジェクト 1個（§4 のエンベロープ参照） |
| 接続 | HTTP/1.1 keep-alive（送信側は `HttpClient` を使い回し） |
| 送信側タイムアウト | 1500 ms — 期限内に応答が無いとリクエスト破棄 |
| 並行性 | セッション中は同時1リクエストのみ（単一ワーカーキューで順序保証） |

### 2.2 Serial（オプション）

| 項目 | 値 |
|---|---|
| 宛先 | プロファイル設定 `NotificationSerialPort` のCOMポート名 |
| ボーレート | 115200 |
| フレーミング | なし（イベント毎に `serialPort.Write(bytes)` を1回。bytes は UTF-8 化した JSON） |
| 方向 | **書き込みのみ** — 送信側は読まない |
| WriteTimeout | 100 ms |
| 動作 | fire-and-forget。失敗はログのみ |
| Hello | **シリアルへは送らない** — Hello は HTTP 限定 |

シリアルストリームでイベント区切りが必要な場合、受信側は JSON オブジェクトの境界（`{` と `}` を文字列リテラル考慮の上でカウント）で判定する必要があります。

### 2.3 即時応答ルール — 最重要

Extension は受信ボディの**処理を始める前に**必ず `200 OK` を返さなければなりません。推奨ハンドラ形:

```
PUT 受信時:
    body = リクエストボディ読取り        # 高速
    enqueue(body)                       # メモリ内キュー（非ブロッキング）
    200 OK 返却（空ボディ）              # ← TTS/LED/ファイルシステムアクセス等よりも前に返す
```

理由: FPVTrackside の送信キューはイベントを順序通りに直列送信します。応答が遅れると後続イベントすべてが最大 1500 ms ずつ遅延し、負荷時（複数パイロット × 複数セクター/秒）には数秒の遅延に発展します。

Extension が**やってはいけない**こと:
- 応答前にボディを検証する（検証は応答後）
- 応答前にダウンストリーム（TTS エンジン、LED COM、ファイル I/O、ネットワーク）を待つ
- 非空のレスポンスボディを返す（送信側は無視するが時間の無駄）

推奨アーキテクチャ: HTTP サーバスレッドはエンキューのみ、別ワーカースレッドが順次処理。

---

## 3. Hello ハンドシェイク

### 3.1 目的

1. Extension に FPVTrackside 起動を通知する
2. FPVTrackside のファイルシステムパスを伝達し、後続イベント中の相対パス（`photoPath` 等）を解決可能にする
3. **Extension が FPVTrackside より先に起動**してもよい設計（Extension は最初の Hello が来るまで待機）

Hello は受信側を「ただのログ吸い込み口」から **FPVTrackside と対等な peer（協調ノード）** に格上げする役割を持ちます。最初の非 Hello イベントが届く時点で Extension は、パイロットメディアの所在（`paths.pilotsDirectory`、`paths.workingDirectory`）、FPVTrackside の表示精度（`decimalPlaces`）、1 ラップのセクター数とどのゲートがラップループか（`timingSystem.splitsPerLap`、各システムの `index`/`role`/`type`）、ホールショット判定や重複ラップ排除に FPVTrackside が使っている閾値（`eventSettings`）をすべて把握済みです。汎用 LED/TTS/スコアボード受信機は、Hello を受けた時点でセクター描画の自動構成、操作者画面と末桁まで一致するラップタイム表示、FPVTrackside と同等のフィルタ判断を行えます — 旧 `RemoteNotifier` の生検出ストリーム単独では達成できなかった機能群です。

### 3.2 FPVTrackside 側の動作

- `ExtensionNotifier` 起動と同時に Hello PUT を即時送信（`t = 0`）
- `2xx` 応答が無い場合、**2000 ms 間隔**で再送
- 最初に `2xx` 応答を受信した時点で**そのセッション中はハートビート完全停止** — FPVTrackside を再起動するまで以後 Hello は送信しない
- Hello は専用の `HttpClient` 呼び出しで送信し、**通常イベントのワーカーキューを経由しない**。通常イベントのスループットには影響しない
- ハートビート中の接続失敗（TCP 拒否、DNS エラー、タイムアウト）はログを出さない（ノイズ抑止）。成功時のみ1回ログ出力

### 3.3 Extension 側に期待される動作

1. Hello 受信時、ただちに `200 OK` を返却
2. `config.json` の `fpvt` ブロックを受信内容で**上書き**（§3.5）
3. 後続イベント処理より前に新パスを Extension 内に反映
4. Hello はレース情報を含まないため、レース状態の変化を起こしてはいけない

### 3.4 Hello のペイロード

```json
{
  "type": "Hello",
  "ts": "2026-05-03T12:34:56.789Z",
  "seq": 1,
  "fpvtVersion": "1.x.x",
  "platform": "Windows",
  "paths": {
    "workingDirectory": "C:\\path\\to\\fpvt\\",
    "baseDirectory":    "C:\\path\\to\\fpvt\\bin\\",
    "eventsDirectory":  "C:\\path\\to\\fpvt\\events\\",
    "profileDirectory": "C:\\path\\to\\fpvt\\data\\default\\",
    "pilotsDirectory":  "C:\\path\\to\\fpvt\\pilots\\"
  },
  "profile": {
    "name": "default"
  },
  "decimalPlaces": 2,
  "timingSystem": {
    "count": 4,
    "primeCount": 1,
    "splitCount": 3,
    "splitsPerLap": 4,
    "allDummy": false,
    "systems": [
      { "index": 0, "type": "LapRFTimingSystem", "role": "Prime" },
      { "index": 1, "type": "LapRFTimingSystem", "role": "Split" },
      { "index": 2, "type": "LapRFTimingSystem", "role": "Split" },
      { "index": 3, "type": "LapRFTimingSystem", "role": "Split" }
    ]
  },
  "eventSettings": {
    "raceStartIgnoreDetections": 0.5,
    "minLapTime": 5.0,
    "primaryTimingSystemLocation": "EndOfLap"
  },
  "channelSettings": {
    "channels": [
      { "band": "Raceband", "number": 1, "frequency": 5658, "colorR": 255, "colorG": 0, "colorB": 0 },
      { "band": "Raceband", "number": 2, "frequency": 5695, "colorR": 0, "colorG": 255, "colorB": 0 }
    ]
  }
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `type` | string | 常に `"Hello"` |
| `ts` | string | ISO-8601 UTC、ミリ秒精度 |
| `seq` | int64 | 単調増加カウンタ（§5） |
| `FpvtVersion` | string | FPVTrackside バージョン |
| `Platform` | string | `"Windows"` / `"macOS"` / `"Linux"` |
| `Paths.WorkingDirectory` | string（絶対パス） | `Directory.GetCurrentDirectory()`。**後続イベント中のすべての相対パス（特に `photoPath`）の解決基準** |
| `Paths.BaseDirectory` | string（絶対パス） | `AppDomain.CurrentDomain.BaseDirectory`。FPVTrackside 実行ファイルの所在 |
| `Paths.EventsDirectory` | string（絶対パス） | 設定 `EventStorageLocation` から解決。相対設定の場合は `WorkingDirectory` 基準で解決 |
| `Paths.ProfileDirectory` | string（絶対パス） | プロファイル別データ: `<WorkingDirectory>/data/<profileName>/`。`ProfileSettings.xml` 等を含む |
| `Paths.PilotsDirectory` | string（絶対パス） | `<WorkingDirectory>/pilots/`。パイロットメディア（写真・動画） |
| `Profile.Name` | string | アクティブプロファイル名。FPVTrackside セッション中は不変 |
| `decimalPlaces` | int | `ApplicationProfileSettings.ShownDecimalPlaces`。Extension が時間（ラップタイム・セクタータイム）をテキスト表示する際の推奨小数桁数。Extension はオーバーライドしてもよいが、FPVTrackside の表示と揃えるため既定値はこれに合わせること |
| `TimingSystem.Count` | int | **設定済み**タイミングシステムの総数（Prime + Split）。接続状態に非依存 |
| `TimingSystem.PrimeCount` | int | 「Prime」システム数（ラップループ検出）。通常 1 |
| `TimingSystem.SplitCount` | int | 「Split」システム数（中間セクター検出） |
| `TimingSystem.SplitsPerLap` | int | **ラップあたりのセクター数。** `SplitCount + 1` に等しい。「+1」は Prime での lap-end 検出（これ自体がラップ最終セクター）の分 |
| `TimingSystem.AllDummy` | bool | 設定済みシステムがすべてダミー/シミュレータの場合 true。設定タイプから判定し、接続状態には依存しない |
| `TimingSystem.Systems[]` | array | システム別リスト。**インデックス番号は `DetectionExt.TimingSystemIndex` と完全に一致**: index `0` が Prime（ラップループ）、index `1..splitCount` が Split（中間セクター）でセクター通過順に並ぶ。配列は `index` 昇順。各要素: `index`（0 始まり）、`type`（C# クラス名、例: `"LapRFTimingSystem"`, `"DummyTimingSystem"`）、`Role`（`"Split"` / `"Prime"`）。受信側は `systems[detection.timingSystemIndex]` で直接ゲートのロールを参照できる |
| `EventSettings.RaceStartIgnoreDetections` | number（秒） | Event 設定「Race Start Ignore Detections」。`RaceStart.ActualStart` から本秒数以内の検出は FPVTrackside 側で破棄される。Extension はレース序盤の「整定中」表示に利用可能 |
| `EventSettings.MinLapTime` | number（秒） | Event 設定「Smart Minimum Lap Time」。本値より速いラップタイムは重複検出として FPVTrackside 側で破棄される |
| `EventSettings.PrimaryTimingSystemLocation` | string | Event 設定「Primary Timing System Location」。`"Holeshot"` または `"EndOfLap"` のいずれか。`Holeshot` = ラップループがスタート位置にあるため、最初の lap-end 通過はホールショット（lap 0 → lap 1 への遷移であって実ラップではない）。`EndOfLap` = ラップループがスタート位置の先にあるため、最初の lap-end 通過がそのまま lap 1 終了（ホールショットは存在しない）。Extension はこの値でホールショット検出を表示／抑制するかを判断する |
| `ChannelSettings.Channels[]` | ChannelInfo[] | イベントで定義されたチャンネル一覧（"Channel Settings"）。各要素は §6.1 の `ChannelInfo`。`colorR/G/B` はイベント設定で割り当てられた表示色。受信側はパイロット未割当時のチャンネル表示やレース外のチャンネル一覧表示に利用可能 |

`TimingSystem` ブロックは FPVTrackside が Hello 送信時点で認識している**設定済みトポロジ**です。接続状態は意図的に含めていません — FPVTrackside 起動直後はほとんどのシステムが接続交渉中で、この時点での connected/disconnected フラグは誤解を招くためです。Extension は `count`, `SplitsPerLap`, 各 `index`/`Role`/`type` をルーティングに利用できます。

すべてのパスは絶対パスで完全に解決済み（`..` 等を含まない）、ホスト OS のパス区切り文字を使用（Windows ではバックスラッシュ、それ以外ではスラッシュ）。実行中の FPVTrackside インスタンスが実際に使用している場所を必ず指します。

### 3.5 Extension の `config.json` スキーマ

Extension は接続状態を永続化することで、FPVTrackside オフライン時でも履歴データへのアクセスを可能にします。

```json
{
  "fpvt": {
    "lastHelloAt": "2026-05-03T12:34:56.789Z",
    "fpvtVersion": "1.x.x",
    "platform": "Windows",
    "paths": {
      "workingDirectory": "...",
      "baseDirectory":    "...",
      "eventsDirectory":  "...",
      "profileDirectory": "...",
      "pilotsDirectory":  "..."
    },
    "profile": { "name": "default" },
    "decimalPlaces": 2,
    "timingSystem": { "...": "§3.4 参照" }
  },
  "extension": {
    "ledComPort": "COM5",
    "ttsEngine": "...",
    "...": "..."
  }
}
```

ルール:
- Hello 受信時は **`fpvt` ブロック全体を上書き** — フィールド単位のマージ禁止（パス構成がセッション間で変わる可能性があるため）
- `Extension` ブロック（およびその他のトップレベルキー）は Hello 更新時も**保持**しなければならない
- アトミック書き込み（一時ファイルに書いてリネーム）でクラッシュ耐性を確保
- 初回 Hello 時に `config.json` が存在しなければ新規作成

### 3.6 パス解決例

後続イベント中のパイロット情報に以下が含まれる場合:
```json
"photoPath": "pilots/jdoe/jdoe.mp4"
```
Extension は次のように解決:
```
absolutePath = path.join(config.Fpvt.Paths.WorkingDirectory, pilot.PhotoPath)
             = "C:\path\to\fpvt\pilots\jdoe\jdoe.mp4"   （Windows の場合）
```

`photoPath` が空または null の場合、そのパイロットにメディアは無く、解決を試みてはならない。

---

## 4. 共通エンベロープ

すべてのイベント（Hello を含む）はこのトップレベル形式を持つ:

```json
{
  "type": "<イベント名>",
  "ts": "<ISO-8601 UTC, ms精度>",
  "seq": <int64>,
  "...": "Type 固有フィールド"
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `type` | string | イベント識別子. ハンドラ振り分けには本フィールドのみを使うこと |
| `ts` | string | 送信側がイベント生成時刻を UTC で記録（送信時刻ではない）. ISO-8601 ミリ秒精度、`Z` サフィックス |
| `seq` | int64 | §5 参照 |

`type` 固有フィールドは `type`/`ts`/`seq` と**同階層**（ラッパでネストしない）.

---

## 5. シーケンス番号と順序保証

- `seq` は送信側が割り当てる単調増加 64bit 整数
- FPVTrackside 起動時に 1 から開始、Hello を含む全イベントで（atomic に）インクリメント
- 1 つの FPVTrackside セッション内では `seq` は減少も重複もしない
- FPVTrackside 再起動を跨ぐと `seq` は 1 にリセット. Extension は `seq` 逆行（または新たな Hello 到来）で再起動を検知可能
- 送信側はイベントを**順序通り**に配送する（トランスポート毎に単一ワーカーキュー）. 順序が処理に重要なら Extension 側のキューも順序を保つこと
- Extension は `seq` で欠落・重複検出やミスオーダーロギングに利用してよい

---

## 6. 共通サブオブジェクト

複数イベント間で共有される構造体。

### 6.1 `ChannelInfo`

```json
{
  "band": "E",
  "number": 1,
  "frequency": 5705,
  "colorR": 255,
  "colorG": 64,
  "colorB": 64
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `band` | string | C# `Band` enum の値名（生）。次のいずれか: `Fatshark`, `Raceband`, `A`, `B`, `E`, `DJIFPVHD`, `SharkByte`（`HDZero` の別名、enum 値同一）, `LowBand`, `Diatone`, `DJIO3`, `DJIO4`, `WalkSnail`、未割当チャンネルは `None`。FPVTrackside のバージョンアップで新しいバンドが追加される可能性があるため、**受信側は値を不透明な文字列として扱い、上記リストが閉じていると仮定してはならない**。 |
| `number` | int | 通常 1〜8 |
| `frequency` | int | MHz |
| `ColorR/G/B` | int (0〜255) | このレースで本チャンネルに割り当てられた表示色 |

### 6.2 `PilotInfoExt`

パイロット情報の標準形. `RaceLoaded`, `NextRace`, `RaceResult`, `PilotRaceState`, `PilotCrashedOut` で使用.

```json
{
  "name": "John Doe",
  "phonetic": "jon doh",
  "discordID": "jdoe#1234",
  "photoPath": "pilots/jdoe/jdoe.mp4",
  "videoFlipped": false,
  "videoMirrored": false,
  "channel": { "...": "ChannelInfo, §6.1 参照" }
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `name` | string | パイロット表示名 |
| `phonetic` | string | TTS 発音ヒント. 明示設定が無い場合は `name` から自動生成 |
| `discordID` | string \| null | 任意 |
| `photoPath` | string \| null | `Paths.WorkingDirectory` 基準の**相対パス**. フィールド名は "Photo" だが動画ファイル（`.mp4` 等）も格納される. 空の可能性あり |
| `VideoFlipped` | bool | 上下反転で表示 |
| `VideoMirrored` | bool | 左右反転で表示 |
| `channel` | ChannelInfo | 本レースで本パイロットに割当てられたチャンネル |

### 6.3 `PositionEntry`

順位スナップショットの 1 行（§7.4）.

```json
{
  "pilotName": "John Doe",
  "position": 1,
  "raceSector": 14,
  "lastDetectionTime": 23.456
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `pilotName` | string | 表示名で識別 |
| `Position` | int | 1 始まり. 同着があれば同位を共有（§7.4） |
| `RaceSector` | int | 到達済み累積セクター index. エンコーディングは `lap × 100 + timingSystemIndex`（§7.4 と用語集を参照）. 値が大きいほど先 |
| `lastDetectionTime` | number（秒） | 順位算定に使用した最後の検出のレース開始からの秒数. 未検出なら `0` |

### 6.4 `StageInfo`

```json
{
  "name": "Qualifying",
  "stageType": "Default"
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `name` | string | ステージ表示名 |
| `StageType` | string | `Default`, `DoubleElimination`, `Final`, `StreetLeague`, `ChaseTheAce`（enum 名） |

現レースのラウンドにステージが無ければ `null`.

### 6.5 `SectorInfo`

```json
{
  "number": 1,
  "length": 0.0,
  "calculateSpeed": false
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `number` | int | コース上のセクター番号（1 始まり） |
| `length` | number | セクター長（メートル）. 不明なら 0 |
| `calculateSpeed` | bool | このセクターで速度を計算するか |

ラップ終端「セクター」は暗黙的（ラップループの最終検出に対応）.

### 6.6 `PilotResultEntry`

`RaceResult.Pilots[]` で使用.

```json
{
  "pilot": { "...": "PilotInfoExt, §6.2 参照" },
  "position": 1,
  "totalLaps": 5,
  "totalTime": 123.456,
  "bestLap": 22.345,
  "bestConsecutive": { "laps": 3, "time": 67.890 },
  "dnf": false
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `pilot` | PilotInfoExt | メディアパスを含む完全なパイロット情報 |
| `Position` | int | 本レースの最終順位（1 始まり） |
| `totalLaps` | int | 完了した有効ラップ数 |
| `totalTime` | number（秒） | 使用した総レース時間（完走時の終了時刻、または DNF ならレース全長） |
| `bestLap` | number（秒） \| null | 単独ベストラップ |
| `bestConsecutive` | object \| null | 連続ベスト（`laps` はイベント設定の連続数、通常 3）. 該当無しなら null |
| `dnf` | bool | DNF（未完走） |

### 6.7 `StageRankingEntry`

`StageRanking.Ranking[]` で使用.

```json
{
  "pilot": { "...": "PilotInfoExt" },
  "position": 1,
  "points": 12,
  "bestLap": 22.345,
  "bestConsecutive": { "laps": 3, "time": 67.890 }
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `pilot` | PilotInfoExt | |
| `position` | int | ステージ内順位（1 始まり） |
| `points` | int \| null | ポイント制イベントの累積ポイント. それ以外なら null |
| `bestLap` | number（秒） \| null | ステージ内ベストラップ |
| `bestConsecutive` | object \| null | ステージ内ベスト連続ラップ |

---

## 7. イベント type 一覧

`Hello`（§3）以外の、Extension が受信し得るすべての `type` 値。

### 7.1 `RaceLoaded`

カレントレースが切り替わったとき発火（マネージャに新レースがロードされたとき）. `RacePreStart` より前に到着.

**`RaceManager.ResetRace` 直後にも再発火する** — リセット対象レースを引数として、同じレースを再選択した場合でも `RaceLoaded` を再送する。結果クリアに伴い受信側のパイロット辞書や派生状態が古くなり得るため、`RaceResult.pilots=[]` の「結果無効化」シグナルと対になり、フル状態を再供給する役割を持つ。受信側は冪等な状態置き換えとして扱うこと（同じ `round`/`race` の連続受信を異常と見なさない）。

```json
{
  "type": "RaceLoaded",
  "ts": "...",
  "seq": 42,
  "round": 3,
  "race": 2,
  "raceType": "Race",
  "scheduledStart": "2026-05-03T13:00:00.000Z",
  "targetLaps": 5,
  "raceLength": 120.0,
  "stage": { "...": "StageInfo または null" },
  "sectors": [ { "...": "SectorInfo" }, ... ],
  "pilots": [ { "...": "PilotInfoExt" }, ... ]
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `round` | int | ラウンド番号 |
| `race` | int | ラウンド内のレース番号 |
| `raceType` | string | `Race` / `TimeTrial` / `AggregateLaps` / `Game` 等（enum 名） |
| `rcheduledStart` | string (ISO-8601 UTC) \| null | 設定済みなら予定開始時刻. Extension はプリレースカウントダウン用に使用 |
| `rargetLaps` | int | 設定ラップ数（時間制のみなら 0） |
| `raceLength` | number（秒） | 設定レース時間（ラップ制のみなら 0） |
| `stage` | StageInfo \| null | ラウンドのステージ |
| `sectors` | SectorInfo[] | コースのセクター構成. ラップ終端「セクター」は暗黙のためここに含めない |
| `pilots` | PilotInfoExt[] | レースに割当てられた全パイロットとチャンネル |

### 7.2 `NextRace`

カレントレース変更時、**次の**レース情報を提供. 次レースのパイロット紹介に使用.

```json
{
  "type": "NextRace",
  "ts": "...",
  "seq": 43,
  "round": 3,
  "race": 3,
  "raceType": "Race",
  "scheduledStart": "2026-05-03T13:05:00.000Z",
  "pilots": [ { "...": "PilotInfoExt" }, ... ]
}
```

次レースが無い場合、`Round`/`Race` は `null`、`pilots` は `[]` で送信.

### 7.3 `RacePreStart` / `RaceStart` / `RaceEnd` / `RaceCancelled` / `RaceTimesUp`

ライフサイクルイベント. 共通ボディ.

```json
{
  "type": "RacePreStart",
  "ts": "...",
  "seq": 44,
  "round": 3,
  "race": 2,
  "raceType": "Race",
  "scheduledStart": "2026-05-03T13:00:05.000Z",
  "actualStart": "2026-05-03T13:00:07.345Z"
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `round` | int | |
| `race` | int | |
| `raceType` | string | |
| `rcheduledStart` | string (ISO-8601 UTC) \| null | `RacePreStart` のみ存在. カウントダウン開始時に決定された**開始予定時刻** |
| `actualStart` | string (ISO-8601 UTC) \| null | `RaceStart` のみ存在. 実際に Go となり時刻が確定した**開始時刻**. レース時刻の `t0` |
| `failure` | bool | `RaceCancelled` のみ存在. システム異常による中止なら true、操作者による中止なら false |

5 つの `type` 値:
- `RacePreStart` — レースアーム完了、**ランダム化されたスタート時刻が確定した直後**、カウントダウン開始直前. **`ScheduledStart` を含む**。`RaceManager.OnRaceStartScheduled`（`OnRacePreStart` ではない）から発火されるため、タイムスタンプは**ランダム化後の真の予定時刻**であり最遅推定値ではない
- `RaceStart` — カウントダウン終了、レースタイマー開始. **`actualStart` を含む**
- `RaceTimesUp` — レース時間経過（必ずしもレース終了ではない. 進行中ラップは継続）
- `RaceEnd` — レース終了（全パイロット終了または停止）
- `RaceCancelled` — レース中止

`RacePreStart.scheduledStart` は `StartRaceInLessThan(MinStartDelay, MaxStartDelay)` が選んだ**正確な予定スタート瞬間** — `[Now + MinStartDelay, Now + MaxStartDelay]` の一様分布乱数で決定されます。本値は wait ループ実行前に配信されるため、受信側はランダム窓全体（およそ `MaxStartDelay − MinStartDelay` から通信遅延数 ms を引いた時間）をスタート合図準備に使えます。これは特に**アクセシビリティ**用途で価値があります: 聴覚障害者・難聴者、あるいは環境騒音で「Go」のビープ音がマスクされる場面でも、`scheduledStart` に固定された LED パネル／ストロボに頼ることで、他のパイロットと同条件で同じスタート合図を受け取れます — イベントがランダムスタート遅延（`MinStartDelay != MaxStartDelay`）を使う場合でも有効です。`scheduledStart`（`RacePreStart`）と `actualStart`（`RaceStart`）を突き合わせれば、運営側はスタートタイミングのジッタを事後監査することも可能です.

### 7.4 `DetectionExt`

最も高頻度なイベント. ゲート検出（セクター通過またはラップ終端）毎に 1 回発火. Extension 用途では既存の `DetectionDetails` を置き換え. レガシー Notifier も有効な場合、両方が wire 上に流れる.

レガシー `DetectionDetails` と比較すると、`DetectionExt` は受信側で従来導出する必要があった情報を**事前計算済みで配信**します:

- **`sectorTime`** — 本検出で終了したセクターの所要時間。レガシーは累積 `Time` のみで、受信側は各パイロットの前回検出を記憶し差分計算する必要があった（途中でイベント欠落があれば破綻）
- **`positionSnapshot[]`** — その瞬間の全パイロット順位（検出対象だけでなく全員）を `Race.GetTrackPosition` 済みの順序で同梱。レガシーは検出パイロット自身の `Position` のみ
- **`raceFinishedForPilot`**, **`valid`**, **`lapTimeSoFar`**, **`raceSector`** — 完走フラグ・フィルタ有効フラグ・進行中ラップタイム・累積順位キー
- **`round` / `race` / `raceType`** — 各検出がレース識別子を持つため、直近の `RaceState` と相関を取る必要がなくなる

**重複排除**: 送信側は detection ID でフィルタするため、同じ検出について本イベントが重複送信されることはない（FPVTrackside 内部で `OnSplitDetection` と `OnLapDetected` の両方が同一検出に対し発火するケースがあっても）。レガシー `RemoteNotifier` には重複排除が**無く**、ラップループ通過のたびに同じ検出が 2 回配信されていました — 受信側でのフィルタリングが必須でした.

```json
{
  "type": "DetectionExt",
  "ts": "...",
  "seq": 99,
  "detectionId": "1f8a2c3d-...",
  "round": 3,
  "race": 2,
  "pilotName": "John Doe",
  "channel": { "...": "ChannelInfo" },
  "timingSystemIndex": 0,
  "isLapEnd": false,
  "lapNumber": 2,
  "sectorIndex": 1,
  "raceSector": 7,
  "raceTime": 38.421,
  "sectorTime": 5.123,
  "lapTimeSoFar": 18.234,
  "position": 2,
  "valid": true,
  "positionSnapshot": [ { "...": "PositionEntry" }, ... ],
  "raceFinishedForPilot": false
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `detectionId` | string (GUID) | 一意 ID. 重複排除に利用可 |
| `Round`, `Race` | int | レース識別 |
| `pilotName` | string | |
| `channel` | ChannelInfo | |
| `timingSystemIndex` | int | 検出元タイミングシステムの index（0 始まり）. 物理ゲートに対応 |
| `isLapEnd` | bool | ラップループ通過なら true、中間セクターなら false |
| `lapNumber` | int | 進行中（`IsLapEnd=true` なら完了直後）のラップ番号（0 始まり） |
| `SectorIndex` | int | ラップ内セクター index（1 始まり）。実装は `(timingSystemIndex % splitsPerLap) + 1`（内部セクター未設定時は `Max(1, timingSystemIndex + 1)`）。Prime/ラップループは `timingSystemIndex=0` なので**ここでの値は `1`** になる。すなわち、ラップエンド通過は「直前ラップの最終セクター」ではなく「次ラップの S1」として番号付けされる。"end-of-lap" セクターのラベルが必要な受信側は本フィールドではなく `splitsPerLap` と `isLapEnd` から導出すること |
| `raceSector` | int | レース開始からの累積セクター index. エンコーディングは `lap × 100 + timingSystemIndex`. `100` は固定の乗数（`splitsPerLap` ではない）であり、インデックス部分は 0 始まりの生 `timingSystemIndex`（Goal = 0）で、上の 1 始まり `sectorIndex` フィールドとは別物. 順序を曖昧なくするためには `splitsPerLap ≤ 100` を前提とする. 順位算定の内部値 — `PositionEntry.RaceSector` と同じ |
| `raceTime` | number（秒） | `RaceStart.ActualStart` からの本検出までの秒数 |
| `sectorTime` | number（秒） \| null | 本検出で終了したセクターの所要時間. 当該パイロットの本ラップ内に先行検出が無ければ null（例: ホールショット） |
| `lapTimeSoFar` | number（秒） | 現ラップ経過時間（`isLapEnd` なら最終ラップタイム） |
| `position` | int | 本検出時点での当該パイロットの順位 |
| `valid` | bool | タイミングシステムやルールでフィルタされた検出は false（可視性のため送信は継続） |
| `PositionSnapshot` | PositionEntry[] | 本検出時点での**全パイロット**の順位スナップショット. 送信側で `Race.GetTrackPosition()` から事前計算済み（セクター進捗を考慮 — ラップ深部にいるパイロットが上位）. Extension 側で**再計算不要**. 長さ = レース内パイロット数 |
| `raceFinishedForPilot` | bool | 本検出が当該パイロットの本レース最終検出（目標ラップ到達等）なら true |

**Position の意味**（`Race.GetTrackPosition` に一致）:
- `raceSector` 降順が第1キー（コース上で先にいる方が上）、検出時刻の昇順が第2キー（同セクターなら早い検出が上）
- 同着（同セクター・同時刻）は同位を共有. 下位パイロットの `Position` 値が複製される. 受信側は `Position == 1` の要素が複数ある可能性を許容すること

### 7.5 `RaceResult`

`ResultManager` がレースの結果状態変化を通知した時に発火。発火コンテキストは 2 種類:

1. **レース終了時の結果確定** — **`RaceEnd` の直前**に送信される（後ではない）。典型的な終了シーケンスは `RaceResult` → `StageRanking`（ステージ所属時のみ） → `RaceEnd`。受信側は `RaceResult` 受信時点でレース終了処理を進めてよく、`RaceEnd` を待つ必要はない
2. **結果クリア** — レースの保存結果がクリアされた時に発火。`RaceManager.ResetRace`（操作者によるリセット）に加え、FPVTrackside 起動時に過去走行済みレースを再ロードした際にも自動的に発火する。このときの `pilots` は **空配列（`[]`）** になり、本イベントは事実上「結果無効化」シグナルとして機能する

```json
{
  "type": "RaceResult",
  "ts": "...",
  "seq": 150,
  "round": 3,
  "race": 2,
  "raceType": "Race",
  "pilots": [ { "...": "PilotResultEntry" }, ... ]
}
```

`pilots` は `Position` 昇順、DNF は末尾。**結果クリア時は `pilots` が `[]` になる** — 結果 UI を描画する受信側は、空リストを「0 名が完走した」ではなく「表示をクリアせよ」と解釈すること。

### 7.6 `StageRanking`

ステージ順位変動時（`ResultManager` の結果再計算時）に送信される。 **直前完了レースがステージに属する場合のみ**（`Race.Round.Stage != null`）.

```json
{
  "type": "StageRanking",
  "ts": "...",
  "seq": 160,
  "stage": { "...": "StageInfo" },
  "ranking": [ { "...": "StageRankingEntry" }, ... ]
}
```

`Ranking` は `Position` 昇順。`RaceResult`（§7.5）と同様、結果クリア（レースリセット・起動時の再ロード）時にも発火する。その場合 `ranking` は空配列または集計値がゼロ／null のエントリを含むため、「ステージ順位無効化」シグナルとして扱うこと。

### 7.7 `PilotCrashedOut`

パイロットがクラッシュアウトと判定されたとき発火（レースディレクター手動、またはスタティック検出による自動）.

```json
{
  "type": "PilotCrashedOut",
  "ts": "...",
  "seq": 110,
  "pilot": { "...": "PilotInfoExt" },
  "manuallySet": true
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `pilot` | PilotInfoExt | チャンネル・メディアパスを含む |
| `manuallySet` | bool | true = レースディレクター手動、false = 自動検出 |

### 7.8 `PilotRaceState`

カレントレースのパイロット名簿が変更されたとき発火（後発参加など）.

```json
{
  "type": "PilotRaceState",
  "ts": "...",
  "seq": 35,
  "round": 3,
  "race": 2,
  "raceType": "Race",
  "pilots": [ { "...": "PilotInfoExt" }, ... ]
}
```

`pilots` は変更**後の現在の全名簿**（追加/削除されたパイロットのみではない）.

### 7.9 `PilotStaggeredStart`

タイムトライアル系レースで FPVTrackside の "Time Trial Staggered Start" 設定が有効な時のみ発火。`RaceManager.StartStaggered` が各パイロットを順次スタートさせる際、**そのパイロットの go 合図が出る瞬間**に 1 件ずつ送信。

```json
{
  "type": "PilotStaggeredStart",
  "ts": "...",
  "seq": 47,
  "round": 1,
  "race": 3,
  "pilot": { "...": "PilotInfoExt (§6.2)" },
  "orderIndex": 0,
  "totalPilots": 4,
  "delaySeconds": 3.0
}
```

| フィールド | 型 | 説明 |
|---|---|---|
| `round` / `race` | int | 対象レースの識別子 |
| `pilot` | PilotInfoExt | スタートするパイロット。`channel.colorR/G/B` をそのまま LED 等の物理出力に流せる |
| `orderIndex` | int | スタート順の 0 始まりインデックス。`0` が最初に go するパイロット |
| `totalPilots` | int | 本レースで staggered スタートする総人数 |
| `delaySeconds` | number（秒） | パイロット間のスタート間隔。`RaceStart.actualStart + (orderIndex+1) * delaySeconds` がそのパイロットの go タイミング |

**スタート順序**: イベント内ベストタイム順位（`LapRecordManager.GetTimePosition`）昇順 → 同順位は周波数昇順。初回ヒート等で PB が無いパイロットは全員同順位扱いとなり、周波数順にスタートする。

**他のスタートモードでは発火しない**:
- 同時スタート（通常 Race）→ `RaceStart` 1 件で完結
- 遅延スタート（`MinStartDelay`/`MaxStartDelay`）→ `RacePreStart` + `RaceStart` で完結

受信側は本イベントの**到着自体**を staggered start のシグナルとして扱える。Hello に staggered フラグは含まれない。

---

## 8. 時刻と数値の表現

- すべての時刻（`ts`, `ScheduledStart`, `actualStart`）: ISO-8601 UTC、ミリ秒精度、`Z` サフィックス. 例: `2026-05-03T12:34:56.789Z`
- すべての時間長（`SectorTime`, `lapTimeSoFar`, `RaceTime`, `bestLap`, `BestConsecutive.Time`, `TotalTime`, `RaceLength`）: JSON 数値、**秒**単位、小数可. `TimeSpan` 文字列は使わない
- すべての順位・カウント: JSON 整数. 注記が無い限り 1 始まり
- 真偽値: JSON `true` / `false`. `0`/`1` は使わない
- 文字列は JSON ペイロード中で UTF-8

---

## 9. 障害時の挙動

- 送信側は HTTP 失敗時にイベントを再送しない. タイムアウト・接続エラー・非 2xx 応答はサイレントに破棄（エラー型変化時のみログ）
- 送信側 HTTP キュー容量は 200 イベント. 満杯時は**新規イベントを破棄**（最古ではない）. Extension が即時 ack + 非同期処理を守る限り発生しない
- 送信側シリアルキュー容量は 50 イベント、同様の破棄ポリシー
- Extension からの再送要求は不可. 欠落時の状態回復は次の `RaceLoaded` / `RaceResult` / `StageRanking` で行う
- Hello は最初の 2xx まで再試行. 以降はセッション中再送なし

---

## 10. 受信側要件サマリ

Extension（あなたの作る外部アプリケーション） は**必ず**以下を満たすこと:

1. FPVTrackside の `NotificationURL` 設定の URL で HTTP `PUT` を待機
2. リクエストボディ処理の**前に** `200 OK`（空ボディ）を返却
3. JSON をパースし `type` フィールドで振り分け
4. **未知の `type` 値はサイレントに無視**（200 OK は通常通り返却）. 将来未知の`type`が追加されることへの対策
5. `Hello` 受信時: `paths` と `Profile`（および `decimalPlaces` と `TimingSystem`）を `config.json` の `fpvt` ブロックにアトミックに永続化
6. `photoPath` 等の相対パスは `config.fpvt.paths.workingDirectory` を基準に解決
7. `seq` リセットを許容（`seq` 減少を送信側再起動と判定し、ログ出力するがクラッシュしない）
8. `detectionId` 重複を許容（送信側でフィルタ済みだが、防御的に重複排除）

Extension は以下を**推奨**:

- 重い処理（TTS、LED 書き込み、ファイルスキャン）は HTTP ハンドラとは別スレッドで実行
- 直近 Hello の `paths` をキャッシュし、FPVTrackside オフライン時にも履歴データスキャンを可能に
- 想定外フィールド・未知 `type` を debug レベルでログ出力

---

## 11. ミニマルテストクライアント（疑似コード）

```
config = read_or_default("config.json")

server = http.create_server(handle)
server.listen(config.Extension?.Port ?? 8765)

queue = bounded_queue(1000)
spawn worker(queue)

def handle(request, response):
    body = request.read_body_sync()       # 軽量
    response.send(200)                    # ★ まず ack
    queue.try_enqueue(body)               # 満杯なら破棄（ログ出力）

def worker(queue):
    while true:
        body = queue.dequeue()
        evt = json.parse(body)
        match evt.Type:
            "Hello":
                config.Fpvt = {
                  "lastHelloAt":   evt.Ts,
                  "fpvtVersion":   evt.FpvtVersion,
                  "platform":      evt.Platform,
                  "paths":         evt.Paths,
                  "profile":       evt.Profile,
                  "decimalPlaces": evt.DecimalPlaces,
                  "timingSystem":  evt.TimingSystem
                }
                write_atomic("config.json", config)
            "RaceLoaded":   on_race_loaded(evt)
            "NextRace":     on_next_race(evt)
            "RacePreStart" | "RaceStart" | "RaceTimesUp" | "RaceEnd" | "RaceCancelled":
                on_race_lifecycle(evt)
            "DetectionExt": on_detection(evt)
            "RaceResult":   on_result(evt)
            "StageRanking": on_stage_ranking(evt)
            "PilotCrashedOut": on_crash(evt)
            "PilotRaceState":  on_roster_change(evt)
            _: log_debug("ignored Type:", evt.Type)
```

これだけで全イベント受信、`config.json` 永続化、ハンドラ振り分けが可能です. 各 `on_*` ハンドラ内ではメディアパス解決を `path.join(config.Fpvt.Paths.WorkingDirectory, pilot.PhotoPath)` で行い、必要に応じて LED/TTS を発火します.

---

## 12. 用語集

| 用語 | 意味 |
|---|---|
| **イベント** | 1 回の HTTP PUT（または 1 回のシリアル書き込み）で配送される 1 個の JSON オブジェクト |
| **セクター** | ラップ内のタイミングチェックポイント. セクター 1 はラップループ通過直後から最初の内側ゲートまで |
| **ラップ終端** | ラップループの通過. ラップを完了させる. 特殊なセクター扱い |
| **RaceSector** | レース開始からの累積セクター index. エンコーディングは `lap × 100 + timingSystemIndex`. `100` は固定の乗数（`splitsPerLap ≤ 100` を前提）であり、インデックス部分は 0 始まりの生 `timingSystemIndex`（Goal = 0）で、1 始まりの `sectorIndex` フィールドとは別物. 順位算定に使用 — 大きいほどコース上で先 |
| **PositionSnapshot** | 1 個の検出時点での順位表全体（送信側で事前計算済み） |
| **Stage** | ラウンドのグルーピング（例: 予選、決勝）. ラウンドはステージに属する場合と属さない場合がある |
| **Heartbeat** | 起動時、Extension の ack を受けるまで繰り返される Hello PUT |
| **即時 ack** | 受信側がボディ処理前に 200 OK を返す義務 |
