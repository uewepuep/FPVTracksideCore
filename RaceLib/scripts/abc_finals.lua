name = "A/B/C Finals"
description = "Top pilots to A Final, next group to B Final, etc. Seeded by points."

function generate(round, pilots, channels, options)
    local max = options.max_per_race

    -- Sort descending: highest points in first group (A Final)
    local sorted = sort_by(pilots, function(p)
        local total = 0
        for _, r in ipairs(get_results(p.id)) do total = total + r.points end
        return -total
    end)

    local bracket_names = { "A", "B", "C", "D", "E", "F", "G", "H" }

    local races = {}
    for i = 1, #sorted, max do
        local race_pilots = {}
        for j = i, math.min(i + max - 1, #sorted) do
            table.insert(race_pilots, sorted[j].id)
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
