# FPVTrackside Lua Scripting

Scripts are `.lua` files placed in the `scripts/` folder. They appear in the **Add Format → From Script** menu and can be assigned to a stage.

---

## Script Structure

```lua
name = "My Format"
description = "A short description shown in the menu"

function generate(pilots, channels, options)
    -- build and return races
    return { race1, race2 }
end
```

`generate()` is called every time a new round is generated. It must return a table of races, where each race is a table of pilot IDs.

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
```

---

## Return Format

Return a table of races. Each race is a table of pilot IDs (strings) or pilot objects.

```lua
return {
    { pilots[1].id, pilots[2].id, pilots[3].id },  -- race 1
    { pilots[4].id, pilots[5].id, pilots[6].id },  -- race 2
}
```

Pilots not included in any race are silently ignored. Channel assignment is handled automatically by the app, preserving each pilot's previous channel where possible.

---

## Helper Functions

### `shuffle(list)`
Returns a randomly shuffled copy of a list.
```lua
local shuffled = shuffle(pilots)
```

### `sort_by(list, fn)`
Returns a sorted copy of a list. `fn(item)` returns the sort key (number or string). Sorts ascending.
```lua
local by_points = sort_by(pilots, function(p) return get_points(p.id) end)
local by_time   = sort_by(pilots, function(p) return get_best_consecutive_laps_event(p.id, 3) end)
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

### `get_points(pilot_id)`
Total points for a pilot up to and including the calling round.
```lua
local pts = get_points(pilot.id)
```

### `get_positions(pilot_id)`
List of finish positions across all results up to the calling round, oldest first.
```lua
local pos = get_positions(pilot.id)  -- e.g. {1, 3, 2, 1}
```

### `get_laps_finished(pilot_id)`
Number of laps the pilot completed in their most recent race.
```lua
local laps = get_laps_finished(pilot.id)
```

### `top_half(pilot_id)`
Returns `true` if the pilot finished in the top half of their race last round.
```lua
if top_half(pilot.id) then
    table.insert(a_final, pilot.id)
end
```

### `get_unflown_pilots(pilot_id)`
Returns a list of pilot objects this pilot has not yet raced against.
```lua
local fresh = get_unflown_pilots(pilot.id)
-- fresh[i].id, fresh[i].name
```

### `get_bracket(pilot_id)`
Returns the bracket string this pilot was in during the calling round.
Possible values: `"None"`, `"Winners"`, `"Losers"`, `"A"`, `"B"`, `"C"` etc.
```lua
if get_bracket(pilot.id) == "Winners" then ... end
```

---

## Lap Time Functions

All times are returned in **seconds** as a decimal number. Returns `0` if the pilot has no qualifying data.

### `get_best_consecutive_laps_stage(pilot_id, lap_count)`
Best consecutive `lap_count` laps within the current stage.
```lua
local best3 = get_best_consecutive_laps_stage(pilot.id, 3)
```

### `get_best_consecutive_laps_event(pilot_id, lap_count)`
Best consecutive `lap_count` laps across the whole event. Use `lap_count = 1` for single-lap PB.
```lua
local pb = get_best_consecutive_laps_event(pilot.id, 1)
```

---

## Channel Functions

### `get_last_channel(pilot_id)`
Returns the channel object the pilot was on last round, or `nil` if they have no previous channel.
```lua
local ch = get_last_channel(pilot.id)
if ch then
    print(ch.id, ch.name, ch.band)
end
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

