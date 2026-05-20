# GATE / LED POST notifications Extension Interface Specification (English)

Version: 1.1  
Direction: FPVTrackside (sender) → Extension (receiver), one-way  
Audience: developers building Extensions or test clients

This document is self-contained: a working test client can be built from this file alone.

---

## 1. Activation and Backward Compatibility

The interface described here is active **only when** the FPVTrackside profile setting `ExtensionMode` is `true`. The setting lives under the **"Gate / LED POST notifications"** category in *Application Profile Settings*, defaults to `false`, and requires application restart to take effect.

When `ExtensionMode = false`:
- No traffic described in this document is generated.
- Legacy `GATE / LED POST notifications` behavior (controlled by `NotificationEnabled`, `NotificationURL`, `NotificationSerialPort`) is preserved verbatim, including any pre-existing bugs.

When `ExtensionMode = true`:
- A separate component (`ExtensionNotifier`) starts and emits the events defined below.
- Legacy `RemoteNotifier` is **suppressed** even if `NotificationEnabled = true`. ExtensionMode supersedes the legacy stream so the two never co-emit on the same URL/COM port.
---

## 2. Transport

### 2.1 HTTP

| Attribute | Value |
|---|---|
| Method | `PUT` |
| Target URL | value of profile setting `NotificationURL` (e.g. `http://127.0.0.1:8765/`) |
| Headers | `Content-Type: application/json; charset=utf-8` |
| Body | exactly one JSON object per request (see §4 envelope) |
| Connection | HTTP/1.1 keep-alive (the sender reuses one `HttpClient`) |
| Sender timeout | 1500 ms — the request is abandoned if no response arrives in time |
| Concurrency | one in-flight request at a time per session (events are delivered in order via a single worker queue) |

### 2.2 Serial (optional)

| Attribute | Value |
|---|---|
| Target | COM port name in profile setting `NotificationSerialPort` |
| Baud rate | 115200 |
| Framing | none (a single `serialPort.Write(bytes)` per event; bytes are the UTF-8 encoded JSON) |
| Direction | **write only** — sender never reads |
| WriteTimeout | 100 ms |
| Behavior | fire-and-forget; failures only logged |
| Hello | **never sent over serial** — Hello is HTTP-only |

If the receiver wants to demarcate events on the serial stream, it must rely on JSON object boundaries (matching `{` … `}` with brace counting and string-aware parsing).

### 2.3 IMMEDIATE RESPONSE RULE — CRITICAL

The Extension **MUST** send `200 OK` **before** doing any processing of the body. The recommended handler shape is:

```
on PUT:
    body = read_request_body()       # very fast
    enqueue(body)                    # in-memory queue, non-blocking
    respond 200 OK with empty body   # ← BEFORE any TTS/LED/disk/etc.
```

Why: FPVTrackside's send queue serializes events in order. If a response is delayed, every subsequent event is delayed too, up to the 1500 ms timeout per event. Under load (multiple sectors per second across many pilots), blocking on TTS or LED write would cascade into multi-second latency.

The Extension SHOULD NOT:
- Validate the body before responding (validate after).
- Wait for downstream services (TTS engine, LED COM port, file I/O, network) before responding.
- Return a non-empty response body (the sender ignores it; sending data wastes time).

Recommended Extension architecture: one HTTP server thread that only enqueues, plus one or more worker threads that drain the queue and perform the slow work.

---

## 3. Hello Handshake

### 3.1 Purpose

1. Tell the Extension that FPVTrackside is up.
2. Deliver FPVTrackside's filesystem paths so the Extension can resolve relative file references found in later events (e.g. `photoPath`).
3. Allow the Extension to be started **before** FPVTrackside (the Extension waits idle until the first Hello arrives).

Hello bootstraps the receiver into a **peer** of FPVTrackside rather than a passive log sink. By the time the first non-Hello event arrives, the Extension already knows where pilot media lives (`paths.pilotsDirectory`, `paths.workingDirectory`), what display precision FPVTrackside is using (`decimalPlaces`), how many sectors a lap has and which gate is the lap-loop (`timingSystem.splitsPerLap`, per-system `index`/`role`/`type`), and the exact thresholds FPVTrackside applies for holeshot and duplicate-lap filtering (`eventSettings`). A generic LED/TTS/scoreboard receiver can therefore auto-configure its sector graphics, render lap times that match the operator's screen down to the last digit, and reproduce FPVTrackside's filter decisions without diverging — none of which is possible from the legacy detection stream alone.

### 3.2 Behavior on the FPVTrackside side

- On `ExtensionNotifier` startup, send a Hello PUT immediately (`t = 0`).
- If no `2xx` response is received, retry every **2000 ms**.
- The first response with status code in the `2xx` range stops the heartbeat **permanently for that session** — there are no further Hellos until FPVTrackside is restarted.
- Hello is sent through a dedicated `HttpClient` call, **not** through the event work queue. It does not interfere with normal event throughput.
- Connection failures (TCP refused, DNS error, timeout) are silently swallowed during the heartbeat phase — no log noise. Only the successful handshake is logged once.

### 3.3 Behavior expected on the Extension side

1. On every Hello receipt, immediately respond `200 OK`.
2. Overwrite the `fpvt` block of `config.json` (see §3.5) with the received data.
3. Make the new paths available to the rest of the Extension before processing further events.
4. The Hello carries no race information — no race-state changes should be triggered.

### 3.4 Hello payload

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

| Field | Type | Description |
|---|---|---|
| `type` | string | always `"Hello"` |
| `ts` | string | ISO-8601 UTC, millisecond precision |
| `seq` | int64 | monotonic counter (see §5) |
| `fpvtVersion` | string | FPVTrackside version string |
| `platform` | string | `"Windows"` \| `"macOS"` \| `"Linux"` |
| `paths.workingDirectory` | string (absolute) | `Directory.GetCurrentDirectory()`. **Base for resolving every relative path in later events** (notably `photoPath`). |
| `paths.baseDirectory` | string (absolute) | `AppDomain.CurrentDomain.BaseDirectory` — where the FPVTrackside binary lives. |
| `paths.eventsDirectory` | string (absolute) | Resolved from setting `EventStorageLocation`. If that setting is relative, it is resolved against `workingDirectory`. |
| `paths.profileDirectory` | string (absolute) | Profile-specific data directory: `<workingDirectory>/data/<profileName>/`. Contains `ProfileSettings.xml` and other per-profile state. |
| `paths.pilotsDirectory` | string (absolute) | `<workingDirectory>/pilots/`. Pilot media (photos and videos). |
| `profile.name` | string | Active profile name. Constant for the lifetime of the FPVTrackside session. |
| `decimalPlaces` | int | `ApplicationProfileSettings.ShownDecimalPlaces`. Recommended number of fractional digits when the Extension renders durations (lap times, sector times) as text. The Extension is free to override but should default to this value to match what FPVTrackside displays. |
| `timingSystem.count` | int | Total number of timing systems **configured** (Prime + Split). Independent of connection state. |
| `timingSystem.primeCount` | int | Number of "Prime" systems (lap-loop detectors). Normally 1. |
| `timingSystem.splitCount` | int | Number of "Split" systems (intermediate sector detectors). |
| `timingSystem.splitsPerLap` | int | **Sectors per lap.** Equals `splitCount + 1`. The "+1" accounts for the lap-end detection at the Prime system, which is itself the last sector of the lap. |
| `timingSystem.allDummy` | bool | True if every configured system is a dummy/simulated timer. Computed from configured types only — does **not** require connections. |
| `timingSystem.systems[]` | array | Per-system list. **Index numbering matches `DetectionExt.timingSystemIndex` exactly**: index `0` is the Prime (lap-loop) system, indices `1..splitCount` are Split (intermediate) systems in sector traversal order. The array is ordered by `index` ascending. Each entry: `index` (0-based), `type` (C# class name e.g. `"LapRFTimingSystem"`, `"DummyTimingSystem"`), `role` (`"Split"` or `"Prime"`). Receivers can look up the role of a detection's gate via `systems[detection.timingSystemIndex]`. |
| `eventSettings.raceStartIgnoreDetections` | number (seconds) | Event setting "Race Start Ignore Detections". Detections within this many seconds after `RaceStart.actualStart` are filtered by FPVTrackside. The Extension can use this to display "settling" indicators during the early race. |
| `eventSettings.minLapTime` | number (seconds) | Event setting "Smart Minimum Lap Time". Lap times faster than this are filtered as duplicate detections by FPVTrackside. |
| `eventSettings.primaryTimingSystemLocation` | string | Event setting "Primary Timing System Location". One of `"Holeshot"` or `"EndOfLap"`. `Holeshot` = the lap-loop sits at the start line, so the very first lap-end crossing is a holeshot (lap 0 → lap 1 transition, not a real lap). `EndOfLap` = the lap-loop is past the start line, so the first lap-end crossing IS the end of lap 1 (no holeshot exists). The Extension uses this to decide whether to display / suppress the holeshot crossing. |
| `channelSettings.channels[]` | ChannelInfo[] | Event-level "Channel Settings": the channels defined for this event. Each entry is a §6.1 `ChannelInfo`. `colorR/G/B` are the per-event assigned display colors. Receivers can use this for un-assigned channel rendering or non-race channel listings. |

The `timingSystem` block describes the **configured topology** as known to FPVTrackside at Hello-send time. Connection state is intentionally excluded: at FPVTrackside startup most systems are still negotiating, so a connected/disconnected flag at this point would be misleading. The Extension can rely on `count`, `splitsPerLap`, and the per-system `index`/`role`/`type` for routing logic.

All paths are absolute, fully resolved (no `..` segments), and use the host OS path separator (backslash on Windows, forward slash elsewhere). They are guaranteed to refer to the same locations the running FPVTrackside instance is using.

### 3.5 Extension `config.json` schema

The Extension persists the FPVTrackside connection state so that historical data remains accessible while FPVTrackside is offline.

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
    "timingSystem": { "...": "see §3.4" }
  },
  "extension": {
    "ledComPort": "COM5",
    "ttsEngine": "...",
    "...": "..."
  }
}
```

Rules:
- The Extension overwrites the **entire `fpvt` block** on every Hello — never merge field-by-field, because path layout may change between sessions.
- The Extension MUST preserve the `extension` block (and any other top-level keys) across Hello updates.
- Write atomically (write to a temp file then rename) to survive crashes.
- If `config.json` does not exist on first Hello, create it.

### 3.6 Path resolution example

A pilot record in a later event contains:
```json
"photoPath": "pilots/jdoe/jdoe.mp4"
```
The Extension resolves to:
```
absolutePath = path.join(config.fpvt.paths.workingDirectory, pilot.photoPath)
             = "C:\path\to\fpvt\pilots\jdoe\jdoe.mp4"   (on Windows)
```

If `photoPath` is empty/null, the pilot has no media and no resolution should be attempted.

---

## 4. Common envelope

Every event — including Hello — uses this top-level shape:

```json
{
  "type": "<event-name>",
  "ts": "<ISO-8601 UTC, ms>",
  "seq": <int64>,
  "...": "type-specific fields"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Event discriminator. Use this and only this to dispatch handlers. |
| `ts` | string | Sender's UTC timestamp at the moment the event was created (NOT when it was sent). Format: ISO-8601 with millisecond precision and `Z` suffix. |
| `seq` | int64 | See §5. |

Type-specific fields are **siblings** of `type`/`ts`/`seq`, not nested under a wrapper.

---

## 5. Sequence numbers and ordering

- `seq` is a monotonically increasing 64-bit integer assigned by the sender.
- It starts at 1 on FPVTrackside startup and is incremented (atomically) for every event, including Hello.
- Within one FPVTrackside session, `seq` never decreases and never repeats.
- Across FPVTrackside restarts, `seq` resets to 1. The Extension can detect a restart by observing `seq` going backwards (or by the new Hello arriving).
- Events are delivered **in order** by the sender (single worker queue per transport). The Extension's own queueing must also preserve order if order matters for processing.
- The Extension MAY use `seq` to detect lost or duplicate events and to log misordering.

---

## 6. Sub-objects (shared across events)

These structures are referenced by multiple event types.

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

| Field | Type | Description |
|---|---|---|
| `band` | string | Raw C# `Band` enum name. One of: `Fatshark`, `Raceband`, `A`, `B`, `E`, `DJIFPVHD`, `SharkByte` (alias `HDZero`, same enum value), `LowBand`, `Diatone`, `DJIO3`, `DJIO4`, `WalkSnail`, or `None` for an unassigned channel. New bands may be added in future FPVTrackside versions; receivers MUST treat the value as an opaque string and not assume the list is closed. |
| `number` | int | 1..8 typical |
| `frequency` | int | MHz |
| `ColorR/G/B` | int (0..255) | Display color assigned for this channel in this race |

### 6.2 `PilotInfoExt`

Used wherever a pilot is described, INCLUDING `RaceLoaded`, `NextRace`, `RaceResult`, `PilotRaceState`, `PilotCrashedOut`.

```json
{
  "name": "John Doe",
  "phonetic": "jon doh",
  "discordID": "jdoe#1234",
  "photoPath": "pilots/jdoe/jdoe.mp4",
  "videoFlipped": false,
  "videoMirrored": false,
  "channel": { "...": "ChannelInfo, see §6.1" }
}
```

| Field | Type | Description |
|---|---|---|
| `name` | string | Pilot display name |
| `phonetic` | string | TTS pronunciation hint. May be auto-generated from `name` if not explicitly set. |
| `discordID` | string \| null | Optional |
| `photoPath` | string \| null | **Relative path** from `paths.workingDirectory`. The field name says "Photo" but it commonly holds video files (e.g. `.mp4`). May be empty. |
| `videoFlipped` | bool | Display the media upside down. |
| `videoMirrored` | bool | Mirror left/right. |
| `channel` | ChannelInfo | Channel allocated to this pilot in this race |

### 6.3 `PositionEntry`

A single row in a position snapshot — see §7.4.

```json
{
  "pilotName": "John Doe",
  "position": 1,
  "raceSector": 14,
  "lastDetectionTime": 23.456
}
```

| Field | Type | Description |
|---|---|---|
| `pilotName` | string | Identifies the pilot by display name |
| `position` | int | 1-based; ties may share a position (see §7.4) |
| `raceSector` | int | Cumulative sector index reached, encoded as `lap × 100 + timingSystemIndex` (see §7.4 and Glossary). Higher = further. |
| `lastDetectionTime` | number (seconds) | Seconds since race start of the last detection used to place this pilot. `0` if not yet detected. |

### 6.4 `StageInfo`

```json
{
  "name": "Qualifying",
  "stageType": "Default"
}
```

| Field | Type | Description |
|---|---|---|
| `name` | string | Stage display name |
| `stageType` | string | One of `Default`, `DoubleElimination`, `Final`, `StreetLeague`, `ChaseTheAce` (raw enum name) |

May be `null` if the current race's round has no stage.

### 6.5 `SectorInfo`

```json
{
  "number": 1,
  "length": 0.0,
  "calculateSpeed": false
}
```

| Field | Type | Description |
|---|---|---|
| `number` | int | 1-based sector number on the track |
| `length` | number | Sector length in meters (0 if unknown) |
| `calculateSpeed` | bool | Whether speed is calculated for this sector |

The lap-end "sector" is implicit — it is the last detection of a lap loop.

### 6.6 `PilotResultEntry`

Used in `RaceResult.Pilots[]`.

```json
{
  "pilot": { "...": "PilotInfoExt, see §6.2" },
  "position": 1,
  "totalLaps": 5,
  "totalTime": 123.456,
  "bestLap": 22.345,
  "bestConsecutive": { "laps": 3, "time": 67.890 },
  "dnf": false
}
```

| Field | Type | Description |
|---|---|---|
| `pilot` | PilotInfoExt | Full pilot info including media paths |
| `position` | int | Final position in this race (1-based) |
| `totalLaps` | int | Number of valid laps completed |
| `totalTime` | number (seconds) | Total race time used (race time when finished, or full race length if DNF) |
| `bestLap` | number (seconds) \| null | Best single lap time |
| `bestConsecutive` | object \| null | Best consecutive-laps time. `laps` is the consecutive count configured for the event (often 3). `null` if not applicable. |
| `dnf` | bool | True if the pilot did not finish |

### 6.7 `StageRankingEntry`

Used in `StageRanking.Ranking[]`.

```json
{
  "pilot": { "...": "PilotInfoExt" },
  "position": 1,
  "points": 12,
  "bestLap": 22.345,
  "bestConsecutive": { "laps": 3, "time": 67.890 }
}
```

| Field | Type | Description |
|---|---|---|
| `pilot` | PilotInfoExt | |
| `position` | int | Position within the stage (1-based) |
| `points` | int \| null | Cumulative stage points if the event uses points scoring; otherwise null |
| `bestLap` | number (seconds) \| null | Stage-best single lap |
| `bestConsecutive` | object \| null | Stage-best consecutive laps |

---

## 7. Event types

This section lists every `type` value the Extension may receive (other than `Hello`, defined in §3).

### 7.1 `RaceLoaded`

Fires when the current race changes (a new race is loaded into the manager). Arrives before `RacePreStart`.

**Also re-fires immediately after `RaceManager.ResetRace`** — the reset race is passed back through `RaceLoaded` even when the operator re-selects the same race. Because clearing results can stale the receiver's pilot dictionary and derived state, this re-fire pairs with the `RaceResult.pilots=[]` "results invalidated" signal to resupply the full state. Receivers must treat this as an idempotent state replace (consecutive deliveries of the same `round`/`race` are normal, not an error).

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
  "stage": { "...": "StageInfo or null" },
  "sectors": [ { "...": "SectorInfo" }, ... ],
  "pilots": [ { "...": "PilotInfoExt" }, ... ]
}
```

| Field | Type | Description |
|---|---|---|
| `round` | int | Round number |
| `race` | int | Race number within the round |
| `raceType` | string | `race` / `TimeTrial` / `AggregateLaps` / `Game` / etc. (raw enum name) |
| `scheduledStart` | string (ISO-8601 UTC) \| null | The scheduled start time, if set. The Extension uses this for pre-race countdown animation. |
| `targetLaps` | int | Configured lap count for the race (0 if time-based only) |
| `raceLength` | number (seconds) | Configured race duration (0 if lap-based only) |
| `stage` | StageInfo \| null | Stage of the round, if any |
| `sectors` | SectorInfo[] | Track sector configuration. The lap-end "sector" is implicit and not listed here. |
| `pilots` | PilotInfoExt[] | All pilots assigned to this race with their channels |

### 7.2 `NextRace`

Fires when the current race changes, providing the **next** race in sequence (the one after the just-loaded race). Used to drive pilot introductions for the upcoming race.

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

If there is no next race, the event is sent with `round`/`race` = `null` and `pilots` = `[]`.

### 7.3 `RacePreStart` / `RaceStart` / `RaceEnd` / `RaceCancelled` / `RaceTimesUp`

Lifecycle events. All share the same body.

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

| Field | Type | Description |
|---|---|---|
| `round` | int | |
| `race` | int | |
| `raceType` | string | |
| `scheduledStart` | string (ISO-8601 UTC) \| null | Present on `RacePreStart` only. The **planned start time** determined when the countdown begins. |
| `actualStart` | string (ISO-8601 UTC) \| null | Present on `RaceStart` only. The **actual start moment** when the race officially begins. |
| `failure` | bool | Present on `RaceCancelled` only. True if cancelled due to system failure rather than operator action. |

The five `type` values:
- `RacePreStart` — race armed, **the randomised start time has just been resolved**, countdown about to begin. **Includes `scheduledStart`.** Fires from `RaceManager.OnRaceStartScheduled` (not `OnRacePreStart`), so the timestamp is the post-randomisation planned moment, not an upper-bound estimate.
- `RaceStart` — countdown over, race timer starts now. **Includes `actualStart`.**
- `RaceTimesUp` — race time elapsed (does not necessarily end the race; pilots may still be completing the in-progress lap)
- `RaceEnd` — race over (all pilots finished or stopped)
- `RaceCancelled` — race aborted

`RacePreStart.scheduledStart` is the **exact planned start instant** chosen by `StartRaceInLessThan(MinStartDelay, MaxStartDelay)` — a uniformly distributed pick in `[Now + MinStartDelay, Now + MaxStartDelay]`. The value is delivered before the wait loop runs, so receivers have the full random window (~`MaxStartDelay − MinStartDelay`, minus a few network/serial milliseconds) to prepare their start cues. This is particularly valuable for **accessibility** scenarios: a pilot who is deaf, hard-of-hearing, or running with ambient noise that masks the audio "GO" beep can rely on an LED panel or strobe anchored to `scheduledStart`, getting the same fair start signal as every other pilot — even when the event uses randomised start delays (`MinStartDelay != MaxStartDelay`). Operators can also audit race-start jitter after the fact by comparing `scheduledStart` (from `RacePreStart`) with `actualStart` (from `RaceStart`).

### 7.4 `DetectionExt`

The most frequent event. Fires once per gate detection (sector pass or lap end). Replaces the legacy `DetectionDetails` for Extension purposes; both may be present on the wire when the legacy notifier is also enabled.

Compared to legacy `DetectionDetails`, `DetectionExt` ships several pre-computed fields that previously had to be derived by the receiver:

- **`sectorTime`** — time spent in the sector that ended at this detection. Legacy delivered only the cumulative `Time`, forcing receivers to remember each pilot's previous detection and compute deltas (with no defense against missed events).
- **`positionSnapshot[]`** — full leaderboard at this instant (every pilot, not just the detected one), already ordered by `Race.GetTrackPosition`. Legacy carried only the detected pilot's own `Position`.
- **`raceFinishedForPilot`**, **`valid`**, **`lapTimeSoFar`**, **`raceSector`** — race-completion flag, filter-validity flag, in-progress lap time, and the cumulative ordering key.
- **`round` / `race` / `raceType`** — each detection identifies its own race, so receivers no longer have to correlate against the most recent `RaceState`.

**De-duplication**: the sender filters by detection ID so the same detection is never emitted twice in this event type, even if FPVTrackside internally fires both `OnSplitDetection` and `OnLapDetected` for the same underlying detection. (Legacy `RemoteNotifier` did **not** deduplicate, so every lap-loop crossing was delivered twice — receivers were responsible for filtering.)

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

| Field | Type | Description |
|---|---|---|
| `detectionId` | string (GUID) | Unique ID. Use to suppress duplicates if needed. |
| `round`, `race` | int | Race identification |
| `pilotName` | string | |
| `channel` | ChannelInfo | |
| `timingSystemIndex` | int | Index of the timing system (0-based) that produced the detection — this corresponds to a physical gate. |
| `isLapEnd` | bool | True for the lap-loop crossing; false for intermediate sectors. |
| `lapNumber` | int | 0-based lap currently being run (or just completed if `isLapEnd`). |
| `sectorIndex` | int | Within-lap sector index (1-based). Computed as `(timingSystemIndex % splitsPerLap) + 1` (or `Max(1, timingSystemIndex + 1)` when no inner sectors are configured). Because the Prime/lap-loop system has `timingSystemIndex=0`, **its `sectorIndex` is `1`** — interpret a lap-end crossing as "S1 of the next lap", not "the final sector of the just-completed lap". Receivers that need an "end-of-lap" sector label should derive it from `splitsPerLap` and `isLapEnd`, not from this field. |
| `raceSector` | int | Cumulative sector index since race start, encoded as `lap × 100 + timingSystemIndex`. Note: the `100` is a fixed multiplier (not `splitsPerLap`) and the index portion is the raw 0-based `timingSystemIndex` (Goal = 0), **not** the 1-based `sectorIndex` field above. Requires `splitsPerLap ≤ 100` for ordering to be unambiguous. Used internally for ordering — same value as `PositionEntry.raceSector`. |
| `raceTime` | number (seconds) | Seconds since `RaceStart.actualStart` for this detection. |
| `sectorTime` | number (seconds) \| null | Time taken to traverse the sector that ended at this detection. `null` if no preceding detection exists for this pilot in this lap (e.g. holeshot). |
| `lapTimeSoFar` | number (seconds) | Time elapsed in the current lap (or final lap time if `isLapEnd`). |
| `position` | int | This pilot's position at this exact detection moment. |
| `valid` | bool | False if the detection was filtered out by the timing system or rules (still emitted for visibility). |
| `positionSnapshot` | PositionEntry[] | **All** pilots' positions at this detection moment. Pre-computed by FPVTrackside from `Race.GetTrackPosition()`, which already accounts for sector progress (a pilot deeper into the lap is ahead). The Extension does **not** need to recompute. Length = number of pilots in the race. |
| `raceFinishedForPilot` | bool | True iff this detection is the last one this pilot will produce in this race (target lap reached, etc.). |

**Position semantics** (matches `Race.GetTrackPosition`):
- Pilots ordered first by `raceSector` descending (further along the course is ahead), then by detection time ascending (earlier detection at same sector is ahead).
- Ties (same sector, same time) may share a position; the lower-positioned pilot's `position` is duplicated. Receivers should accept that two entries may have `Position == 1`.

### 7.5 `RaceResult`

Fires whenever `ResultManager` reports a result change for a race. Two contexts:

1. **End-of-race finalization** — fired **immediately before `RaceEnd`** (not after). The typical end-of-race sequence is `RaceResult` → `StageRanking` (if the race belongs to a stage) → `RaceEnd`. Treat `RaceResult` as a leading indicator that the race is wrapping up; do not wait for `RaceEnd` to render results.
2. **Result clear** — fired when the race's stored results are cleared. This happens during `RaceManager.ResetRace` (operator action) and also automatically at FPVTrackside startup when a previously-run race is reloaded into the manager. In this case `pilots` is **empty (`[]`)** and the event is essentially a "results invalidated" signal.

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

`pilots` is sorted ascending by `position`. DNF pilots appear at the bottom. **`pilots` is `[]` when the event represents a result clear** — receivers that render a result UI should treat an empty list as "clear the display" rather than "0 pilots finished".

### 7.6 `StageRanking`

Fires when stage ranking changes (after `ResultManager` recomputes results), but **only** when the just-finished race belongs to a stage (`Race.Round.Stage != null`).

```json
{
  "type": "StageRanking",
  "ts": "...",
  "seq": 160,
  "stage": { "...": "StageInfo" },
  "ranking": [ { "...": "StageRankingEntry" }, ... ]
}
```

`ranking` is sorted ascending by `position`. Like `RaceResult` (§7.5), this event also fires when results are cleared (race reset or startup re-load); in that case `ranking` may be empty or contain entries with zero/null aggregates — treat it as a "stage ranking invalidated" signal.

### 7.7 `PilotCrashedOut`

Fires when a pilot is marked crashed out (manually by the race director or automatically by static-detection).

```json
{
  "type": "PilotCrashedOut",
  "ts": "...",
  "seq": 110,
  "pilot": { "...": "PilotInfoExt" },
  "manuallySet": true
}
```

| Field | Type | Description |
|---|---|---|
| `pilot` | PilotInfoExt | Includes channel and media paths |
| `manuallySet` | bool | True = race director, False = automatic detection |

### 7.8 `PilotRaceState`

Fires when pilots are added to or removed from the current race (e.g., late additions).

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

`pilots` is the **complete current roster** of the race after the change — not just the added/removed pilot.

### 7.9 `PilotStaggeredStart`

Fires only when a TimeTrial race runs with FPVTrackside's "Time Trial Staggered Start" enabled. `RaceManager.StartStaggered` starts each pilot in turn; this event is emitted **at the exact instant the pilot gets the go signal**, once per pilot.

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

| Field | Type | Description |
|---|---|---|
| `round` / `race` | int | Race identifiers |
| `pilot` | PilotInfoExt | The pilot getting the go signal. `pilot.channel.colorR/G/B` can be sent straight to LED hardware. |
| `orderIndex` | int | 0-based start order. `0` is the first pilot to go. |
| `totalPilots` | int | Number of pilots in the staggered start for this race. |
| `delaySeconds` | number (seconds) | Inter-pilot start delay. Each pilot's go moment is `RaceStart.actualStart + (orderIndex+1) * delaySeconds`. |

**Start order**: ascending by event-wide best-time rank (`LapRecordManager.GetTimePosition`), then ascending by channel frequency as a tiebreaker. In the first heat (no PB yet), every pilot has the same rank and frequency-order applies.

**Not emitted for other start modes**:
- Simultaneous start (regular Race) → `RaceStart` alone is sufficient.
- Delayed start (`MinStartDelay` / `MaxStartDelay`) → `RacePreStart` + `RaceStart` carry everything.

The arrival of this event itself is the staggered-start signal for receivers. No staggered flag is sent in Hello.

---

## 8. Time and number formats

- All wall-clock timestamps (`ts`, `scheduledStart`, `actualStart`): ISO-8601 UTC, millisecond precision, `Z` suffix. Example: `2026-05-03T12:34:56.789Z`.
- All durations (`sectorTime`, `lapTimeSoFar`, `raceTime`, `bestLap`, `bestConsecutive.time`, `totalTime`, `raceLength`): JSON numbers in **seconds**, fractional. No `TimeSpan` strings.
- All positions and counts: JSON integers. 1-based unless noted.
- Booleans: JSON `true`/`false`, never `0`/`1`.
- Strings are UTF-8 in the JSON payload.

---

## 9. Failure semantics

- The sender does not retry events on HTTP failure. A timeout, connection error, or non-2xx response causes the event to be dropped silently (logged once per error type).
- The sender's HTTP queue capacity is 200 events. If the queue is full, **new events are dropped** (not the oldest). Under normal Extension behavior (immediate ack, async processing) this never occurs.
- The sender's serial queue capacity is 50 events with the same drop policy.
- The Extension cannot request a resync. If events are missed, the Extension recovers state at the next `RaceLoaded`/`RaceResult`/`StageRanking`.
- Hello retries until first 2xx; after that, never retries within the session.

---

## 10. Receiver requirements summary

A conforming Extension(The external applications you create) MUST:

1. Listen for HTTP `PUT` on the URL configured in FPVTrackside's `NotificationURL`.
2. Respond `200 OK` (empty body) **before** processing the request body.
3. Parse the JSON, dispatch on the `type` field.
4. **Silently ignore** unknown `type` values (return 200 OK as normal). Measures to address the possibility of unknown `type`s being added in the future.
5. On `Hello`: persist `paths` and `profile` (along with `decimalPlaces` and `timingSystem`) into `config.json`'s `fpvt` block atomically.
6. Resolve `photoPath` and any other relative paths against `config.fpvt.paths.workingDirectory`.
7. Tolerate `seq` resets (treat any decreasing `seq` as a sender restart; log but do not crash).
8. Tolerate duplicate `detectionId` (deduplicate defensively, even though the sender filters).

A conforming Extension SHOULD:

- Run the slow work (TTS, LED writes, file scans) on threads other than the HTTP handler.
- Cache `paths` from the last Hello so that historical event-data scans work even when FPVTrackside is offline.
- Log unexpected fields and unknown `type` values at debug level for diagnosis.

---

## 11. Minimal test client (pseudocode)

```
config = read_or_default("config.json")

server = http.create_server(handle)
server.listen(config.extension?.port ?? 8765)

queue = bounded_queue(1000)
spawn worker(queue)

def handle(request, response):
    body = request.read_body_sync()       # cheap
    response.send(200)                    # ★ ack first
    queue.try_enqueue(body)               # drop if full (logged)

def worker(queue):
    while true:
        body = queue.dequeue()
        evt = json.parse(body)
        match evt.type:
            "Hello":
                config.fpvt = {
                  "lastHelloAt":               evt.ts,
                  "fpvtVersion":               evt.fpvtVersion,
                  "platform":                  evt.platform,
                  "paths":                     evt.paths,
                  "profile":                   evt.profile,
                  "decimalPlaces":             evt.decimalPlaces,
                  "timingSystem":              evt.timingSystem,
                  "eventSettings":             evt.eventSettings
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
            _: log_debug("ignored type:", evt.type)
```

This is enough to receive every event, persist `config.json`, and dispatch handlers. Each `on_*` handler can resolve media paths via `path.join(config.fpvt.paths.workingDirectory, pilot.photoPath)` and trigger LED/TTS as needed.

---

## 12. Glossary

| Term | Meaning |
|---|---|
| **Event** | A single JSON object delivered over one HTTP PUT (or one serial write). |
| **Sector** | A timing checkpoint within a lap. Sector 1 starts at the lap loop and ends at the first inner gate. |
| **Lap end** | The crossing of the lap loop, completing a lap. Treated as a special sector. |
| **RaceSector** | Cumulative sector index since race start, encoded as `lap × 100 + timingSystemIndex`. The `100` is a fixed multiplier (assumes `splitsPerLap ≤ 100`); the index portion is the raw 0-based `timingSystemIndex` (Goal = 0), not the 1-based `sectorIndex` field. Used to rank pilots — higher means further along the course. |
| **PositionSnapshot** | The full leaderboard at the moment of a single detection, pre-computed by the sender. |
| **Stage** | A grouping of rounds (e.g. Qualifying, Final). A round may or may not belong to a stage. |
| **Heartbeat** | The repeated Hello PUT during startup until the Extension acks. |
| **Immediate ack** | The receiver's obligation to send 200 OK before processing the body. |
