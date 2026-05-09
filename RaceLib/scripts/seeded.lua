name = "Seeded"
description = "Pilots seeded across races by incoming order. Best pilot spread across races via serpentine draft."
author = "uewepuep"

function generate(round, pilots, channels, options)

    local max = options.max_pilots_per_race

    -- Work out how many races we need
    local race_count = math.ceil(#pilots / max)

    -- Build empty race buckets
    local race_pilots = {}
    for i = 1, race_count do
        race_pilots[i] = {}
    end

    -- Serpentine (snake draft): fill left-to-right then right-to-left
    local direction = 1
    local race_idx  = 1
    for _, pilot in ipairs(pilots) do
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
