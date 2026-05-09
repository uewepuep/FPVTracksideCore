name = "A/B/C Finals"
description = "Top pilots to A Final, next group to B Final, etc. Uses incoming pilot order."
author = "uewepuep"

function generate(round, pilots, channels, options)
    local max = options.max_pilots_per_race

    local bracket_names = { "A", "B", "C", "D", "E", "F", "G", "H" }

    local races = {}
    for i = 1, #pilots, max do
        local race_pilots = {}
        for j = i, math.min(i + max - 1, #pilots) do
            table.insert(race_pilots, pilots[j].id)
        end
        local bracket_index = math.ceil(i / max)
        local bracket = bracket_names[bracket_index] or tostring(bracket_index)
        table.insert(races, {
            bracket = bracket,
            pilots  = minimise_channel_change(race_pilots)
        })
    end

    return races
end
