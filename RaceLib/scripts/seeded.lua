name = "Seeded"
description = "Fastest pilots seeded across races by qualifying lap time."

function generate(round, pilots, channels, options)

    local max = options.max_per_race

    -- Sort slowest-first so the serpentine puts the fastest pilot in the
    -- final slot of race 1 and the second-fastest in the final slot of race 2,
    -- spreading speed evenly across the field.
    local sorted = sort_by(pilots, function(p)
        local t = get_best_consecutive_laps(p.id, 1)
        -- pilots with no qualifying time go last (slowest)
        if t == 0 then return 999999 end
        return t
    end)

    -- Work out how many races we need
    local race_count = math.ceil(#sorted / max)

    -- Build empty race buckets
    local race_pilots = {}
    for i = 1, race_count do
        race_pilots[i] = {}
    end

    -- Serpentine (snake draft): fill left-to-right then right-to-left
    local direction = 1
    local race_idx  = 1
    for _, pilot in ipairs(sorted) do
        table.insert(race_pilots[race_idx], pilot.id)
        race_idx = race_idx + direction
        if race_idx > race_count then
            race_idx  = race_count
            direction = -1
        elseif race_idx < 1 then
            race_idx  = 1
            direction = 1
        end
    end

    local races = {}
    for _, rp in ipairs(race_pilots) do
        table.insert(races, minimise_channel_change(rp))
    end

    return races
end
