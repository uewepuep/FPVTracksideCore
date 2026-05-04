name = "Points Grouped"
description = "Pilots sorted by points. Lowest scorers race together, highest scorers race together."

function generate(pilots, channels, options)
    local max = options.max_per_race

    -- Sort ascending: lowest points races in the earliest groups
    local sorted = sort_by(pilots, function(p)
        return get_points(p.id)
    end)

    local races = {}
    for i = 1, #sorted, max do
        local race_pilots = {}
        for j = i, math.min(i + max - 1, #sorted) do
            table.insert(race_pilots, sorted[j].id)
        end
        table.insert(races, minimise_channel_change(race_pilots))
    end

    return races
end
