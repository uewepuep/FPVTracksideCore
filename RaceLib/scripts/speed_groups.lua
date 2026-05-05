name = "Speed Groups"
description = "Pilots grouped by best consecutive lap time. Fastest pilots in group 1, next in group 2, etc."

function generate(round, pilots, channels, options)

    if not is_first_round() then
        pilots = pilots_with_results(pilots)
    end

    local sorted = sort_by(pilots, function(p)
        local t = get_best_consecutive_laps(p.id, options.target_laps)
        if t == 0 then return 999999 end
        return t
    end)

    local n          = #sorted
    local race_count = options.race_count
    local base       = math.floor(n / race_count)
    local extra      = n % race_count  -- first `extra` groups get one extra pilot

    local races = {}
    local idx = 1
    for i = 1, race_count do
        local size = base + (i <= extra and 1 or 0)
        local group = {}
        for _ = 1, size do
            if sorted[idx] then
                table.insert(group, sorted[idx].id)
                idx = idx + 1
            end
        end
        if #group > 0 then
            table.insert(races, minimise_channel_change(group))
        end
    end

    return races
end
