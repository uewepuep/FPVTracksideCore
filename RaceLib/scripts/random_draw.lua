name = "Random Draw"
description = "Randomly distributes pilots across heats"

function generate(pilots, channels, options)
    local shuffled = shuffle(pilots)
    local heats = {}

    for i = 1, options.heat_count do
        heats[i] = {}
    end

    for i, pilot in ipairs(shuffled) do
        local heat_index = ((i - 1) % options.heat_count) + 1
        table.insert(heats[heat_index], pilot.id)
    end

    return heats
end
