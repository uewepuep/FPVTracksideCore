name = "Random Draw"
description = "Randomly distributes pilots across heats"

function generate(round, pilots, channels, options)
    local shuffled = shuffle(pilots)
    local heats = {}

    for i = 1, options.race_count do
        heats[i] = {}
    end

    for i, pilot in ipairs(shuffled) do
        local heat_index = ((i - 1) % options.race_count) + 1
        table.insert(heats[heat_index], pilot.id)
    end

    return heats
end
