# FPVTrackside Lua Scripting

Scripts are `.lua` files placed in the `scripts/` folder. They appear in the **Add Format → From Script** menu and can be assigned to a stage.

---

## Script Structure

```lua
name = "My Format"
description = "A short description shown in the menu"

function generate(round, pilots, channels, options)
    -- optionally edit the round
    round.name = "My Custom Round"
    -- build and return races
    return { race1, race2 }
end
```

`generate()` is called every time a new round is generated. It must return a table of races, where each race is a table of pilot IDs.

---

## Input: `round`

The round being generated. Read-only fields are informational; writable fields are applied back after `generate()` returns.

```lua
round.number       -- int, read-only. The round number within its event type.
round.stage_index  -- int, read-only. 1-based position of this round within the stage, counting all rounds regardless of event type.
round.name         -- string, writable. Override the round's display name.
round.event_type   -- string, writable. One of: "Race", "TimeTrial", "Practice", "Freestyle", "Endurance", "CasualPractice", "Game"
round.game_type_name  -- string, writable. Game type identifier.
```

`round.stage_index` is the most reliable way to reason about position within a stage. It counts all rounds in order regardless of event type, so it stays correct even when a stage mixes Race and TimeTrial rounds.

---

## Input: `pilots`

A list of pilot objects for this round.

```lua
pilots[i].id    -- string, unique pilot ID
pilots[i].name  -- string, pilot's display name
```

---

## Input: `channels`

A list of available channel objects for this round.

```lua
channels[i].id    -- string, unique channel ID
channels[i].name  -- string, display name e.g. "R1", "F4"
channels[i].band  -- string, band name e.g. "RaceBand", "Fatshark"
```

---

## Input: `options`

```lua
options.race_count    -- int, number of races to generate
options.max_per_race  -- int, maximum pilots per race (number of channels)
options.target_laps   -- int, the event's configured target lap count
options.pb_laps       -- int, the event's configured PB lap count
```

---

## Round Offsets

Many helper functions accept an optional **round offset** argument. Offsets are relative to the round currently being generated:

| Offset | Meaning |
|--------|---------|
| `0`    | Current round (being generated — no races exist yet) |
| `-1`   | Previous round (the default when omitted) |
| `-2`   | Two rounds back |
| `-3`   | Three rounds back |

Rounds are ordered by their insertion order within the event, ignoring event type. This means a stage mixing Race and TimeTrial rounds is counted correctly without ambiguity.

---

## Return Format

Return a table of race objects. Each race can be a flat list of pilot IDs, or a table with optional fields:

```lua
-- Simple flat list
return {
    { pilots[1].id, pilots[2].id, pilots[3].id },
    { pilots[4].id, pilots[5].id, pilots[6].id },
}

-- With bracket and target laps
return {
    { bracket="Losers",  target_laps=3, pilots={ pilots[1].id, pilots[2].id } },
    { bracket="Winners", target_laps=5, pilots={ pilots[3].id, pilots[4].id } },
}
```

Both formats can be mixed freely. Pilots not included in any race are silently ignored. Channel assignment is handled automatically by the app, preserving each pilot's previous channel where possible.

### Race object fields

```lua
race.bracket      -- string, optional. "None", "Winners", "Losers", "A", "B", "C" ... (default "None")
race.target_laps  -- int, optional. Override the lap count for this specific race.
race.pilots       -- table of pilot IDs. If omitted, the array part of the race table is used.
```

---

## Helper Functions

### `shuffle(list)`
Returns a randomly shuffled copy of a list.
```lua
local shuffled = shuffle(pilots)
```

### `map(list, fn)`
Returns a new table with `fn` applied to each item.
```lua
local positions = map(get_results(pilot.id), function(r) return r.position end)
```

### `filter(list, fn)`
Returns a new table containing only items where `fn(item)` returns true.
```lua
local finished = filter(get_results(pilot.id), function(r) return not r.dnf end)
```

### `sum(list [, fn])`
Returns the sum of `fn(item)` for each item. If no `fn`, sums the items directly.
```lua
local total_points = sum(get_results(pilot.id), function(r) return r.points end)
```

### `average(list [, fn])`
Returns the average of `fn(item)` across all items. Returns `0` if the list is empty.
```lua
local avg_position = average(get_results(pilot.id), function(r) return r.position end)
```

### `min(list [, fn])`
Returns the item with the lowest `fn(item)` value. If no `fn`, compares items directly.
```lua
local best_result = min(get_results(pilot.id), function(r) return r.time end)
```

### `max(list [, fn])`
Returns the item with the highest `fn(item)` value. If no `fn`, compares items directly.
```lua
local worst_result = max(get_results(pilot.id), function(r) return r.position end)
```

### `sort_by(list, fn)`
Returns a sorted copy of a list. `fn(item)` returns the sort key (number or string). Sorts ascending.
```lua
local by_points = sort_by(pilots, function(p) return get_points(p.id) end)
local by_time   = sort_by(pilots, function(p) return get_best_consecutive_laps(p.id, 3) end)
```

### `history(pilot_id_a, pilot_id_b)`
Returns the number of times two pilots have been in the same race across all races.
```lua
local times = history(pilots[1].id, pilots[2].id)
```

### `minimise_channel_change(race)`
Reorders pilots within a single race so the app's channel assignment gives as many pilots as possible their previous channel. Call on each race before returning.
```lua
race1 = minimise_channel_change(race1)
```

---

## Pilot Data Functions

### `get_results(pilot_id [, from_offset [, to_offset]])`
Returns a list of result objects for a pilot, oldest first. Defaults to all results up to the previous round. Use `from_offset` and `to_offset` to narrow the window — both are round offsets and both are optional.

```lua
local results = get_results(pilot.id)           -- all results up to previous round
local recent  = get_results(pilot.id, -3)       -- from 3 rounds ago to previous round
local window  = get_results(pilot.id, -5, -2)   -- specific window
local one     = get_results(pilot.id, -2, -2)   -- exactly 2 rounds ago
```

Each result object has:
```lua
result.points    -- int,    points scored
result.position  -- int,    finish position (1 = first)
result.laps      -- int,    laps completed
result.dnf       -- bool,   true if did not finish
result.round     -- int,    round number this result is from
result.time      -- number, total race time in seconds
```

### `has_any_results([round_offset])`
Returns `true` if any race in the given round has ended. Defaults to the previous round (`-1`).
```lua
if has_any_results() then
    pilots = pilots_with_results(pilots)
end
if has_any_results(-2) then ... end
```

### `all_results_in([round_offset])`
Returns `true` if all races in the given round have ended. Defaults to the previous round. Useful to hold off generating a new round until the previous one is fully complete.
```lua
if has_any_results() and not all_results_in() then
    return nil  -- not ready yet, leave existing races unchanged
end
if all_results_in(-2) then ... end
```

### `has_result(pilot_id [, round_offset])`
Returns `true` if the pilot has a completed race in the given round. Defaults to the previous round.
```lua
if has_result(pilot.id) then
    -- pilot has actually flown this round
end
if has_result(pilot.id, -2) then ... end
```

### `pilots_with_results(pilots [, round_offset])`
Returns a filtered copy of the pilots list containing only those with a completed race in the given round. Defaults to the previous round.
```lua
pilots = pilots_with_results(pilots)
-- or filter by a specific round
pilots = pilots_with_results(pilots, -2)
```

### `top_half(pilot_id [, round_offset])`
Returns `true` if the pilot finished in the top half of their race in the given round, or if their race has not ended yet. Returns `false` if their race ended and they finished in the bottom half. Defaults to the previous round.
```lua
if top_half(pilot.id) then
    table.insert(a_final, pilot.id)
end
if top_half(pilot.id, -2) then ... end
```

### `is_first_round()`
Returns `true` if this is the first round of the current stage (i.e. there is no previous round in this stage). Equivalent to checking `round.stage_index == 1`.
```lua
if not is_first_round() then
    pilots = pilots_with_results(pilots)
end
```

### `get_unflown_pilots(pilot_id)`
Returns a list of pilot objects this pilot has not yet raced against.
```lua
local fresh = get_unflown_pilots(pilot.id)
-- fresh[i].id, fresh[i].name
```

### `get_bracket(pilot_id [, round_offset])`
Returns the bracket string this pilot was in during the given round. Defaults to the previous round.
Possible values: `"None"`, `"Winners"`, `"Losers"`, `"A"`, `"B"`, `"C"` etc.
```lua
if get_bracket(pilot.id) == "Winners" then ... end
if get_bracket(pilot.id, -2) == "Losers" then ... end
```

---

## Lap Time Functions

All times are returned in **seconds** as a decimal number. Returns `0` if the pilot has no data.

### `get_best_consecutive_laps(pilot_id, lap_count [, from_offset [, to_offset]])`
Best consecutive `lap_count` laps across all races, optionally filtered to a round range. Use `lap_count = 1` for single-lap PB.
```lua
local pb     = get_best_consecutive_laps(pilot.id, 1)           -- all time
local best3  = get_best_consecutive_laps(pilot.id, 3)           -- all time, 3 consecutive laps
local recent = get_best_consecutive_laps(pilot.id, 1, -3)       -- last 3 rounds
local window = get_best_consecutive_laps(pilot.id, 1, -5, -2)   -- specific window
```

---

## Channel Functions

### `get_last_channel(pilot_id [, round_offset])`
Returns the channel object the pilot was on in the given round, or `nil` if they have no channel for that round. Defaults to the previous round.
```lua
local ch = get_last_channel(pilot.id)
if ch then
    print(ch.id, ch.name, ch.band)
end
local ch_prev = get_last_channel(pilot.id, -2)
```

### `get_interfering_channels(channel_id)`
Returns a list of channel objects that interfere with the given channel (same frequency band conflicts).
```lua
local blocked = get_interfering_channels(channels[1].id)
```

### `count_channel_changes(pilot_id)`
Returns how many times this pilot has been assigned a different channel across all races in the event.
```lua
local changes = count_channel_changes(pilot.id)
```
