name = "Random Draw"
description = "Randomly distributes pilots across heats"
author = "uewepuep"

function generate(round, pilots, channels, options)
    local shuffled   = shuffle(pilots)
    local race_count = math.ceil(#pilots / options.max_pilots_per_race)
    local heats = {}

    for i = 1, race_count do
        heats[i] = {}
    end

    for i, pilot in ipairs(shuffled) do
        local heat_index = ((i - 1) % race_count) + 1
        table.insert(heats[heat_index], pilot.id)
    end

    return heats
end
