# FPVTrackside REST API

Version: 1 (`/api/v1`)
Audience: anyone building a tool that reads or drives FPVTrackside — timing integrations, stream graphics, club systems, race-day helpers.

The API exposes the event that is open in FPVTrackside: its pilots, channels, rounds, races, laps, every timing-gate crossing and the results, plus race control. Anything you change through it shows up on the FPVTrackside screen straight away, exactly as if the race director had done it.

**The interactive console is the quickest way in.** With FPVTrackside running, open

```
http://localhost:8080/api/v1/docs
```

Every endpoint is there with a form, ready-made example requests filled in from the event you have open, a **Send** button, the response, and the same request as curl, JavaScript, Python or Go. It needs no internet connection.

The machine-readable description is at `/api/v1/openapi.json` (OpenAPI 3) — import it into Postman or Insomnia, or generate a client from it. Both the console and that document are generated from the server's own route table, so they always match the build you are running.

---

## 1. Switching it on

The API is served by FPVTrackside's existing web server.

| Setting (Settings → Web) | What it does |
|---|---|
| **Auto start HTTP Server** | Starts the web server (and so the API) on port 8080. Needs a restart. |
| **HTTP Server race controls enabled** | Allows race start/stop and race selection through the API (`/race-control/*`). Without it those return `403 race_control_disabled`; everything else still works. Needs a restart. |

## 2. Trying it without a venue: the sandbox

`ApiSandbox` runs the real FPVTrackside race engine and web server with no window, against a generated demo event: eight pilots, a finished time-trial round with split-gate crossings, and a race round ready to run. Races run on FPVTrackside's built-in simulated timer, which produces laps — including the occasional false read, so there is something to correct.

```
dotnet run --project ApiSandbox -- --port 8080
```

Then open `http://localhost:8080/api/v1/docs`. Options: `--data DIR` (where the demo event lives, default `./sandbox-data`) and `--fresh` (throw it away and generate a new one). The sandbox also prints a `[ui]` line for every notification the FPVTrackside screen would act on, so you can see what each call would refresh.

## 3. Conventions

| | |
|---|---|
| Base URL | `http://<fpvtrackside machine>:8080/api/v1` |
| Format | JSON in and out, UTF-8, camelCase field names |
| IDs | GUIDs, or `ext:<n>` for an external system's ID (see *Meeting sync*) |
| Pilots | anywhere a pilot is expected you may use the pilot's ID **or** exact name (`Wheelzfpv`) |
| Channels | ID, label as FPVTrackside shows it (`R1`, `F4`, `L3`), or frequency (`5658`) |
| Times | ISO 8601 in the FPVTrackside machine's local time, with its UTC offset: `2026-10-11T09:14:05.123+10:00` |
| Race time | `raceTimeMs` / `raceTimeSeconds` count from the race start |
| Enums | strings (`"Race"`, `"TimeTrial"`, `"Raceband"`) |

### Errors

Every error is a JSON body plus an HTTP status:

```json
{ "ok": false, "code": "conflict", "error": "the race is running" }
```

| Status | `code` | Meaning |
|---|---|---|
| 400 | `bad_request` | The request is wrong; `error` says how. Nothing was changed. |
| 401 | `unauthorized` | A change from another machine without the right token. |
| 403 | `forbidden` | A change from another machine, and no token is configured. |
| 403 | `race_control_disabled` | Race control over HTTP is switched off in settings. |
| 404 | `not_found` | No such race / pilot / round / detection, or no such endpoint. |
| 501 | `not_available` | Needs the FPVTrackside window, which this server doesn't have (the sandbox). |
| 502 | `obs_unavailable` | OBS didn't answer. |
| 405 | `method_not_allowed` | Wrong HTTP method; the `Allow` header lists the right ones. |
| 409 | `conflict` | Not possible right now (the race is running, a channel is taken, rules are locked…). |
| 503 | `busy` | FPVTrackside didn't get to the request within 15 s. Safe to retry. |
| 500 | `internal` | A bug — please report it with the request. |

### POST with no body

Send `{}` or at least `Content-Length: 0`. .NET's HTTP server rejects a POST that carries neither with `411 Length Required` before FPVTrackside sees it (`curl -X POST` with no `-d` does this; most HTTP libraries send `Content-Length: 0` on their own).

## 4. Who can change things

* **Reading** is open to anyone who can reach the machine on the network — the same as the event files the web server has always served.
* **Changes** are accepted from the FPVTrackside machine itself without any token.
* **Changes from another machine** need a token. Set the environment variable `FPV_API_TOKEN` on the FPVTrackside machine to a long random value and restart FPVTrackside, then send it with each request:

  ```
  Authorization: Bearer <token>
  ```

  `X-Api-Token: <token>` also works. With no token set, other machines can only read.

`GET /api/v1` tells you whether the caller may make changes (`canWrite`).

There is no HTTPS: FPVTrackside's web server is plain HTTP, so keep it on the venue network.

## 5. Endpoints

All paths are under `/api/v1`. **W** = changes data.

### Info and event
| | | |
|---|---|---|
| `GET /` | | Server, version, open event, whether you may make changes, where the docs are |
| `GET /event` | | The event: name, type, laps, PB laps, race length, channels |
| `PATCH /event` | W | Change name, laps, PB laps or race length. The last three are refused while rules are locked |
| `GET /channels` | | The event's channels in order |

### Pilots
| | | |
|---|---|---|
| `GET /pilots` | | Every pilot in the event, with their default channel |
| `GET /pilots/database` | | Every pilot FPVTrackside knows (its pilot database, across events), each with `inEvent` |
| `POST /pilots` | W | Add a pilot (`name`, optional `channel`). An existing pilot of that name is reused |
| `GET /pilots/{pilot}` | | One pilot (ID or name) |
| `PATCH /pilots/{pilot}` | W | Name, first/last name, Discord ID, aircraft, catch phrase, practice pilot, Velocidrone ID, default channel |
| `DELETE /pilots/{pilot}` | W | Take a pilot off the event's list (their flown races are kept) |
| `GET /pilots/{pilot}/races` | | Every race the pilot is in |

### Rounds
| | | |
|---|---|---|
| `GET /rounds` | | Rounds in running order |
| `POST /rounds` | W | Add an empty round (`eventType`) |
| `GET /rounds/{id}` | | One round |
| `PATCH /rounds/{id}` | W | Give the round a custom name |
| `DELETE /rounds/{id}` | W | Delete a round (unrun races go with it; finished ones are kept) |
| `GET /rounds/{id}/races` | | The round's races |

### Races
| | | |
|---|---|---|
| `GET /races` | | All races in running order. Filters: `?state=pending\|running\|finished`, `?round=`, `?pilot=` |
| `POST /races` | W | Create a race in a round (or a new round) with its line-up; optionally select it |
| `GET /races/current` | | The race selected in FPVTrackside |
| `GET /races/{id}` | | A race with all its laps and results |
| `DELETE /races/{id}` | W | Delete a race (not while it is running) |
| `POST /races/{id}/reset` | W | Clear a race's laps and results so it can be re-run |
| `PUT /races/{id}/pilots` | W | Replace the line-up (`[{pilot, channel}]`) |
| `POST /races/{id}/pilots` | W | Seat one pilot (moves them if already seated) |
| `DELETE /races/{id}/pilots/{pilot}` | W | Take a pilot out of a race |

Line-up changes are refused while a race is running. Pilots named in a line-up who aren't in the event are added to it. A pilot without a channel gets their usual one if it's free, else the first free event channel.

### Laps and timing crossings
| | | |
|---|---|---|
| `GET /races/{id}/laps` | | Every lap, valid and invalid (`?pilot=` to filter) |
| `GET /races/{id}/detections` | | Every crossing: lap lines and split gates, valid and invalid (`?pilot=`) |
| `POST /races/{id}/laps` | W | Add a lap-line crossing |
| `PATCH /races/{id}/detections/{detectionId}` | W | Count or discount a crossing |
| `POST /races/{id}/lap-corrections` | W | Invalidate false lap lines and add missed ones in one step |
| `GET /races/{id}/results` | | Results, best first |

These work on **any** race — running or finished, selected or not. After every change FPVTrackside rebuilds the pilot's laps, the race results and the lap records, and its screens update.

A **lap** is the time between two lap-line crossings (lap 0, the holeshot, runs from the start to the first). Invalidating a lap-line crossing therefore merges the two laps either side of it; validating it splits them again. That is why lap edits are made on *crossings* (`detectionId`) rather than on laps.

**Adding a crossing** — give exactly one of:

```jsonc
{ "pilot": "Wheelzfpv", "time": "2026-10-11T09:14:05.123+10:00" }   // wall-clock time
{ "pilot": "Wheelzfpv", "raceTimeMs": 52300 }                        // from the race start
{ "pilot": "Wheelzfpv", "anchorDetectionId": "…", "offsetMs": 9740 } // after another crossing
```

The anchored form places the crossing relative to another crossing of the same pilot (usually a split gate) and is immune to clock differences between machines — use it whenever the time comes from another system. Adding a crossing within 250 ms of an existing valid one does nothing, so retries are safe.

**Lap corrections** combine both edits, check everything before changing anything, and can be repeated harmlessly:

```http
POST /api/v1/races/{raceId}/lap-corrections
Content-Type: application/json

{
  "pilotId": "97999f7d-95b6-443c-acab-8cd23732d909",
  "invalidate": ["8db8cbfc-f059-4331-9f4e-55d746f5e977"],
  "add": [ { "anchorDetectionId": "64e2d57d-ef70-4811-8274-9c4a6b38c51e", "offsetMs": 12470 } ]
}
```

```json
{ "ok": true, "error": "", "invalidated": 1, "added": 1, "laps": [ … the pilot's laps after the change … ] }
```

Invalidations are recorded as `Marshall`. This is the call external lap-review tools make — for example the AUFPV lap review, through its dd-pits venue agent.

### Race control
| | | |
|---|---|---|
| `GET /race-control` | | What the race-director screen is doing: `none` / `ready` / `starting` / `running` / `finished`, the selected and next race, elapsed time |
| `PUT /race-control/current` | W | Select a race (`{"raceId": "…"}`, or `null` to clear) |
| `POST /race-control/next` · `/previous` | W | Select the next / previous race |
| `POST /race-control/start` | W | Start the selected race — the same as pressing Start, countdown and sounds included |
| `POST /race-control/stop` | W | End the running race |
| `POST /race-control/cancel` | W | Cancel a start countdown |

All of these need *HTTP Server race controls enabled*. Start can be refused (409) for the same reasons the Start button would be: no race selected, the race already ran (reset it first), no timing system ready. If FPVTrackside asks the race director to confirm something (e.g. more pilots than the timer supports), the question appears on the FPVTrackside screen and the API returns 409.

### Meeting sync (driving FPVTrackside from another system)
For a system that owns the meeting — the AUFPV platform through its dd-pits venue agent, a club database, a spreadsheet script — and wants FPVTrackside to match it.

**External IDs.** Pilots, rounds and races carry an `externalId` (an integer, 0 = none): the other system's own ID for them. Set it when creating (`POST /pilots`, `/rounds`, `/races`) or later (`PATCH`), filter by it (`?externalId=` on `GET /pilots`, `/rounds`, `/races`), and address anything by it wherever an ID goes: `/races/ext:5001`, `/rounds/ext:9001/races`, a line-up seat `{"pilot": "ext:101"}`. A pilot's externalId is the same field FPVTrackside shows as *MultiGP ID*, and a race's is *External Race ID* — the one AUFPV round pastes already fill, so results of a finished race can be routed back by it.

| | | |
|---|---|---|
| `GET /meeting` | | Everything in one read: event, channels, pilots, rounds, every race with line-up, laps and results, the selected race and race-control state. `?laps=false` for just the schedule |
| `POST /meeting/sync` | W | Make FPVTrackside match a plan (below). Idempotent |
| `PATCH /races/{id}` | W | Set a race's `externalId` or `targetLaps` |
| `PUT /event/channels` | W | Set the event's channels in order (`["R1","R3","R6","R8"]`). FPVTrackside reloads the event to apply them, so the API is away for a few seconds |
| `PATCH /event` | W | Also takes `eventType`, `minLapTimeSeconds`, `packLimit`, `start` |

A **meeting plan** lists pilots, then rounds each with their races in running order, all keyed by externalId:

```json
{
  "pilots": [ { "externalId": 101, "name": "Wheelzfpv", "channel": "R1" } ],
  "rounds": [
    { "externalId": 9001, "eventType": "Race", "name": "Winners Round 1",
      "races": [ { "externalId": 5001, "pilots": [ { "pilot": "ext:101", "channel": "R1" } ], "targetLaps": 3 } ] }
  ],
  "removeMissing": false,
  "dryRun": false
}
```

* The whole plan is checked first; a bad plan changes nothing (400 with the reason).
* Pilots are matched by externalId, then by name (the match then takes the externalId); missing ones are created and added to the event; names, details and default channels are updated.
* Rounds and races are matched by externalId and created when missing; a pending race's line-up and target laps are updated when they differ.
* **A race that has started or finished is never changed** — it comes back as `locked`. So the plan can be re-sent at any time during the meeting, e.g. after every result, and only the races not yet run follow it.
* `removeMissing: true` deletes races that have an externalId, haven't been run and aren't in the plan (e.g. a race the other system no longer needs). Races without an externalId — ones the race director added by hand — are never touched.
* `dryRun: true` reports what would change and changes nothing.

The response lists every pilot, round and race with its FPVTrackside `id` and what happened: `created`, `updated`, `unchanged`, `removed`, `locked` or `conflict` (with a `message`), plus a `changed` count. Sending the same plan twice gives `changed: 0`.

To pull results back, read `GET /meeting` (or `GET /races?state=finished`) and match races by `externalId`.

### Interface
Everything on the FPVTrackside screen. These need the FPVTrackside window (the sandbox answers `501 not_available`).

| | | |
|---|---|---|
| `GET /app` | | The tab and live scene showing, every tab (enabled or not) and scene, the pilot list / worm / laps / subtitles / mute switches, auto-runner state and timer, which race-director buttons are showing, timing systems and OBS |
| `PUT /app/tab` | W | Switch tab: `Rounds`, `Live`, `Replay`, `Lap Records`, `Lap Count`, `Points`, `Channel List`, `RSSI Analyser`, `Photo Booth`, `Patreons` |
| `PUT /app/scene` | W | Show the Live tab on a scene: `PreRace`, `VideoCheck`, `Race`, `FinishLine`, `RaceResults`, `EventStatus`, `Commentators`, `Clear`, or `Auto` |
| `PATCH /app/view` | W | Pilot list, worm, laps, subtitles, TTS/WAV mute, auto-runner pause / add seconds, channel tiles (`channelVisibility` ±n), re-order tiles |
| `POST /app/replay` | W | Open a race on the Replay tab |
| `POST /app/channel-groups/{n}/crashed-out` | W | Toggle a channel group's tile |
| `GET /app/commands` | | Every command by name: race buttons, tabs, scenes, view switches, sounds, OBS, settings dialogs, new windows, web pages, data actions |
| `POST /app/commands/{name}` | W | Run one (e.g. `tab-lapcount`, `scene-raceresults`, `announce-race`, `open-timing-settings`, `window-replay`). Handy for a Stream Deck: one URL, no body |
| `GET /app/events` | | The events FPVTrackside has |
| `POST /app/events/{id}/open` | W | Switch to another event |
| `POST /app/event-selector` · `/app/restart` | W | Back to event selection / reload the event |

The commands marked `raceControl` in the list (start, stop, reset, delete…) need *HTTP Server race controls enabled*, like `/race-control/*`.

### More race control
| | | |
|---|---|---|
| `POST /race-control/start` | W | Also takes `{"videoCheck": true}` to go through the video-check scene, as the Start button does |
| `POST /race-control/stop` | W | Now the Stop button exactly: ends the race or cancels the countdown, and stops the recording |
| `POST /race-control/emergency-stop` | W | Emergency stop |
| `POST /race-control/clear` | W | The Clear button |
| `POST /race-control/resume` | W | Resume a finished race |
| `POST /race-control/start-stop-next` | W | Whatever the start/stop shortcut would do now |

### Sound
| | | |
|---|---|---|
| `GET /sound` | | The sounds `play` accepts |
| `POST /sound/play` | W | `{"sound": "announceRace"}` — also `announceResults`, `hurryUp`, `untilRaceStart`, `timeRemaining`, `raceOver`, `flag`, `custom1`…`custom5`, any sound name, or `{"sound": "say", "text": "…"}` to speak any words |
| `POST /sound/stop` | W | Stop the current sound |

### OBS
FPVTrackside's OBS remote control (obs-websocket), set up in *OBS Remote Control Settings* or here.

| | | |
|---|---|---|
| `GET /obs` · `PATCH /obs` | ·W | Connected / active, the triggers; pause or resume automatic OBS changes, reconnect |
| `GET /obs/scenes` · `/sources` · `/filters` · `/hotkeys` | | Ask OBS |
| `PUT /obs/scene` | W | Switch OBS's program scene |
| `POST /obs/trigger` | W | Fire one of FPVTrackside's triggers (`PreRaceScene`, `RaceEnd`, `PointsTab`…): runs every rule set for it |
| `POST /obs/hotkey` · `/obs/filter` | W | Press an OBS hotkey by name; switch a source filter |
| `GET /obs/config` · `PUT /obs/config` | ·W | Host, port, password (never returned) and the trigger → action rules; saved and applied at once |

### Settings
| | | |
|---|---|---|
| `GET /settings` | | Settings groups (`application`, `auto-runner`) and every field: type, allowed values, category, whether it needs a restart |
| `GET /settings/{group}` | | Current values. Secrets come back as `********` |
| `PATCH /settings/{group}` | W | Change just the fields sent; saved as the dialog's OK does. `needsRestart` says whether `POST /app/restart` is needed |

Timing, channel, points, sound, theme and video settings have their own editors: open them on screen with the `open-…` commands.

### Export
| | | |
|---|---|---|
| `GET /export/pbs` · `/export/raw-laps` · `/export/race-results` | | The tables behind the Export menu, as `{columns, rows}`. `?decimals=` |

### Stages
A stage is a set of rounds run as one competition: double elimination, MultiGP top-4 qualifiers, bracket progression, ladders and points brackets, with the same logic as the AUFPV platform. Each race in a stage is flown as a normal FPVTrackside race. When the first race is sent, its rounds are grouped under an FPVTrackside stage of the same name, and each race gets its FPVTrackside race bracket (`Winners`, `Losers`, or `A`, `B`, … for the bracket formats). Stages are saved with the event (`StagePlans.json` in the event folder). The **stages web app** at `/stages` (e.g. `http://localhost:8080/stages`) is built entirely on these endpoints.

| Endpoint | | |
|---|---|---|
| `GET /stages/formats` | | The formats (`elimination`, `qualifier`, `mainsprogression` = bracket progression, `ladder`, `mains` = points brackets) and which `setup` fields each reads |
| `GET /stages/seeding` | | Pilots ranked by best consecutive laps in the time-trial rounds. `?laps=3`, `?round=<id>`, `?pilots=all` to include pilots with no time |
| `GET /stages` | | The event's stages |
| `POST /stages` | W | Create: `{"name": "Open Class Finals", "format": "elimination", "pilots": [top seed first…], "setup": {…}, "seatChannels": ["R1","R3","R6","R8"]}`. Anything left out of `setup` uses a default. A setup the engine can't build is refused with 409 and the reason |
| `GET /stages/{id}` | | Every race (state, line-up, channels, result, linked FPVTrackside race), standings, champion and the diagram |
| `PATCH /stages/{id}` | W | Rename, change seat channels, switch automatic results or automatic sending on or off |
| `DELETE /stages/{id}` | W | Delete. Races already sent stay in FPVTrackside |
| `POST /stages/{id}/send` | W | Create every *ready* race in FPVTrackside, one round per stage round |
| `POST /stages/{id}/sync` | W | Take finished results from FPVTrackside's timing for every linked race; reports any that couldn't be applied and why |
| `POST /stages/{id}/races/{race}/send` | W | Send one race; `{"makeCurrent": true}` also selects it on the race-director screen |
| `POST /stages/{id}/races/{race}/result` | W | Record a finishing order by hand: `{"placement": [pilot IDs or names…], "lock": true}`. Only the pilots who advance are required |
| `POST /stages/{id}/races/{race}/reset` | W | Clear a result; its pilots are taken back out of later races. Refused (409) once a later race that depends on it has run |
| `POST /stages/{id}/races/{race}/lock` | W | `{"locked": true}` keeps a result against timing; `false` hands it back to timing |

Race ids here are the stage's own (`W1-1` = Winners round 1 race 1, `L2-1` = Losers round 2 race 1, `CTA-3`, `A:W1-1` for bracket A in bracket progression) — URL-encode them. A race's `state` is `waiting` (line-up not known), `ready`, `sent` (in FPVTrackside, not run), `running`, `flown` (finished in FPVTrackside, result not applied), `finished` or `void` (not needed).

**Sending automatically.** With `autoSend` on (a switch on the stage page, or `"autoSend": true` when creating or patching), each race is created in FPVTrackside the moment its whole line-up is known: round 1 straight away, later races as results come in. It only creates new races; it never re-seats one already sent. A race the stage then doesn't need (the rest of a chase-the-ace series once the champion is decided) is taken back out if it hasn't been flown. A race still waiting on another race's result is never sent, automatically or by hand.

**Results from timing.** With automatic results on (the default), when a linked race ends — or its results change — the finishing order from FPVTrackside's timing is applied to the stage and the next races fill in. A result entered by hand is locked so a later timing change can't overwrite it. Channels: a pilot keeps the channel from their previous race when it's free; otherwise seats fill in `seatChannels` order, then FPVTrackside's free channels.

## 6. How changes reach the screen

The web server runs on its own thread, but FPVTrackside's race engine and screens run on the game loop. Every API call — reads included — is handed to the game loop and runs there, between frames, through the same RaceLib calls the screen's own buttons use. So:

* changes are never half-applied while the screen is drawing;
* each change raises the same notifications the screen listens for (pilot list, rounds view, live channel tiles, laps, results), so it updates immediately;
* for the race selected on the race-director screen, seat changes go through the screen's own add/remove-pilot path, so its live channel tiles follow.

A call waits up to 15 s for the game loop; if FPVTrackside is frozen (a modal dialog, a long load) it answers `503 busy` rather than hanging.

## 7. Versioning

`/api/v1` only grows: new endpoints and new fields may appear, existing ones keep their meaning. Ignore fields you don't know. A breaking change would come as `/api/v2`, with v1 kept alongside it.
