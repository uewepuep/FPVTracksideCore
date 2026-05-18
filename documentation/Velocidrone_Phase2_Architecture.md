# Phase 2: Velocidrone Integration – Architecture Proposal

## 1. Summary of Architecture

The integration adds a **Velocidrone websocket client** as a separate service (not an `ITimingSystem`) that:

- Connects to Velocidrone over WebSocket
- Sends race control commands (start, stop, activate pilots)
- Consumes race/lap events and feeds them into the existing race model via a new `RaceManager` path that bypasses frequency-based detection
- Uses **pilot UID mapping** (Velocidrone `uid` ↔ FPV Trackside `Pilot`) instead of frequency
- Is **opt-in** and fully isolated from normal real-world timing

No `ITimingSystem` implementation is used because:
- Velocidrone identifies pilots by UID, not RF frequency
- The existing `OnDetection(freq, ...)` path maps frequency → pilot; Velocidrone events have no frequency
- A simulator-specific path (`AddSimulatorLap`) keeps the design clean and avoids frequency hacks

---

## 2. Component Overview

| Component | Purpose | Location |
|-----------|---------|----------|
| **VelocidroneWebSocketClient** | Low-level websocket connect/send/receive, JSON parsing | `ExternalData/Velocidrone/` |
| **VelocidroneSettings** | Host, port, auto-reconnect, persisted per profile | `ExternalData/Velocidrone/` |
| **VelocidroneService** | Orchestrates connection, pilot mapping, race control, event ingestion | `ExternalData/Velocidrone/` |
| **Pilot.VelocidroneUID** | Optional string to map FPV Trackside pilot ↔ Velocidrone uid | `RaceLib/Pilot.cs` |
| **RaceManager.AddSimulatorLap** | Accepts pilot+channel+time+lapNumber directly, creates Detection, calls AddLap | `RaceLib/RaceManager.cs` |
| **TimingSystemType.Velocidrone** | New enum value for simulator detections | `Timing/ITimingSystem.cs` |
| **VelocidroneConnectionNode** | UI for connection config, status, connect/disconnect | `UI/Nodes/` |
| **VelocidronePilotMappingNode** | UI for mapping pilots and pushing to simulator | `UI/Nodes/` |

---

## 3. File-Level Plan

### 3.1 New Files

| File | Description |
|------|-------------|
| `ExternalData/Velocidrone/VelocidroneWebSocketClient.cs` | WebSocket client, send commands, receive messages, parse JSON (protocol assumptions isolated here) |
| `ExternalData/Velocidrone/VelocidroneSettings.cs` | Host, Port, AutoReconnect, persisted via IOTools in profile |
| `ExternalData/Velocidrone/VelocidroneProtocol.cs` | DTOs for inbound/outbound messages (racestatus, racedata, pilotlist, etc.) |
| `ExternalData/Velocidrone/VelocidroneService.cs` | Service layer: connect/disconnect, start/stop/activate, pilot mapping, event ingestion → RaceManager |
| `UI/Nodes/Velocidrone/VelocidroneConnectionNode.cs` | Connection UI (host, port, connect, status) |
| `UI/Nodes/Velocidrone/VelocidronePilotMappingNode.cs` | Pilot mapping UI, push pilots to simulator |
| `UI/Nodes/Velocidrone/VelocidroneRaceControlNode.cs` | Start/stop race buttons, status display |
| `UI/Nodes/Velocidrone/VelocidronePanelNode.cs` | Container/tab for all Velocidrone UI |

### 3.2 Modified Files

| File | Changes |
|------|---------|
| `RaceLib/Pilot.cs` | Add `VelocidroneUID` (string) for mapping |
| `RaceLib/RaceManager.cs` | Add `AddSimulatorLap(Pilot, Channel, DateTime, int lapNumber, bool isLapEnd)` |
| `RaceLib/RaceManager.cs` | Add `OnSimulatorRaceStarted`, `OnSimulatorRaceStopped` or use existing race events |
| `Timing/ITimingSystem.cs` | Add `TimingSystemType.Velocidrone` |
| `RaceLib/EventManager.cs` | Add `VelocidroneService VelocidroneService { get; }`, create in ctor, dispose |
| `UI/BaseGame.cs` or equivalent | Wire VelocidroneService to EventManager, surface UI access |
| `DB/Pilot.cs` | Add `VelocidroneUID` (string) for persistence via ReflectionTools.Copy |

---

## 4. Data Flow

### 4.1 Lap Ingestion

```
Velocidrone WS message (racedata with uid, lap, time, gate)
    → VelocidroneWebSocketClient parses
    → VelocidroneService.OnRacedataReceived
    → Map uid → Pilot (via Pilot.VelocidroneUID or PilotChannel order)
    → Get Channel from CurrentRace.GetChannel(pilot)
    → RaceManager.AddSimulatorLap(pilot, channel, time, lapNumber, isLapEnd)
    → Creates Detection(TimingSystemType.Velocidrone, ...)
    → AddLap(detection)  [existing path]
```

### 4.2 Race Control Outbound

```
Operator clicks "Start Race"
    → VelocidroneService.StartSimulatorRace()
    → VelocidroneWebSocketClient.Send({"command": "startrace"})
    → Velocidrone starts; sends racestatus with raceAction: "start"
    → VelocidroneService can optionally sync FPV Trackside race start (or operator starts both)
```

### 4.3 Pilot Activation

```
Operator maps pilots, clicks "Push Pilots"
    → VelocidroneService.ActivatePilots(pilotUids)
    → Client.Send({"command": "activate", "pilots": ["uid1","uid2",...]})
```

---

## 5. Pilot Mapping Strategy

1. **Primary**: Use `Pilot.VelocidroneUID` if set and match exactly to Velocidrone `uid`.
2. **Fallback**: If `getpilots` returns `pilotlist`, match by name (case-insensitive). Store matched uid in `Pilot.VelocidroneUID` for next time.
3. **Slot-based (last resort)**: If no uid/name match and race has N pilots, assume Velocidrone slot order matches FPV Trackside pilot order. Log warning; isolate this in `VelocidroneService.MapUidToPilot()`.

Matching logic lives in `VelocidroneService`; no logic in core `Race`/`Pilot` beyond the `VelocidroneUID` property.

---

## 6. Protocol Assumptions (from rh-velocidrone reference)

All assumptions are isolated in `VelocidroneWebSocketClient` and `VelocidroneProtocol.cs`:

| Assumption | Value | Notes |
|------------|-------|-------|
| Endpoint | `ws://{host}:{port}/velocidrone` | Default port 60003 |
| Outbound JSON | `{"command": "startrace"}` | Start race |
| | `{"command": "abortrace"}` | Stop/abort race |
| | `{"command": "activate", "pilots": ["uid1",...]}` | Activate pilots |
| | `{"command": "getpilots"}` | Request pilot list |
| Inbound `racestatus` | `raceAction`: "start", "abort", "race finished" | Race state |
| Inbound `racedata` | Per-pilot: `uid`, `lap`, `time`, `gate`, `finished` | Lap/race data |
| Inbound `FinishGate` | Gate crossing data | May include holeshot logic |
| Keepalive | Ping every ~5 seconds | Prevent disconnect |
| Max pilots | 8 | Velocidrone constraint |

Any deviation should be handled defensively and logged.

---

## 7. Simulator Transport Abstraction

**Recommendation: Do not introduce a formal interface yet.**

- Only Velocidrone is supported; no second simulator in scope
- A future `ISimulatorRaceSource` could wrap `VelocidroneService` if Liftoff or another sim is added
- Keeping it concrete avoids speculative abstraction

---

## 8. Settings Persistence

- `VelocidroneSettings` stored per profile: `IOTools.Read/Write(profile, "VelocidroneSettings.xml")`
- Mirror pattern from `TimingSystemSettings.Read(profile)`
- Load when EventManager/Profile initializes; save on change

---

## 9. Migration / Compatibility

- **Existing users**: No changes. Velocidrone features are behind UI that is only shown when simulator mode or a "Velocidrone" menu is enabled
- **Pilot.VelocidroneUID**: New optional property; default null/empty. No migration of existing pilots needed
- **DB**: If Pilot is serialized via a DB model, add `VelocidroneUID` to the persistence layer

---

## 10. Logging Strategy

| Area | Log level | Example |
|------|-----------|---------|
| Connect/disconnect | Info | "Velocidrone connected to ws://...:60003/velocidrone" |
| Command sent | Debug | "Sent command: startrace" |
| Message received | Debug | "Received racestatus: raceAction=start" |
| Pilot match failure | Warning | "No pilot found for Velocidrone uid xyz" |
| Malformed payload | Warning | "Unknown message type: foo" |
| Lap recorded | Debug | "Simulator lap: Pilot X, lap 3, time ..." |

Use existing `Logger` facility (e.g. `Logger.RaceLog`, or a dedicated `Logger.VelocidroneLog` if available).

---

## 11. UI Integration

- Add a "Velocidrone" or "Simulator" tab/panel, accessible from the main UI (e.g. from `TracksideTabbedMultiNode` or equivalent)
- Tab contains:
  1. Connection (host, port, connect, status, auto-reconnect)
  2. Pilot mapping (list of FPV pilots vs Velocidrone pilots, map/sync, push to sim)
  3. Race control (start/stop, sync status)

Operator flow: Configure → Connect → Map Pilots → Push Pilots (if supported) → Start Race (from Trackside or sync with sim) → Laps flow in automatically.

---

## 12. Race State Synchronization

- **Race started in Velocidrone**: On `racestatus` with `raceAction: "start"`, optionally auto-start or highlight that simulator race started (operator may have started from Trackside first)
- **Race stopped/aborted in Velocidrone**: On `raceAction: "abort"` or `"race finished"`, reflect in UI; do not force-end Trackside race if operator wants to keep it for review
- **Laps**: Always ingest; AddSimulatorLap runs through normal validation (race start ignore window, etc.)

---

## 13. Out of Scope (Explicitly Not in Phase 2)

- Formal `ISimulatorRaceSource` interface
- Liftoff or other simulators
- Changing core race logic for non-simulator flows
- Video/replay integration with Velocidrone
