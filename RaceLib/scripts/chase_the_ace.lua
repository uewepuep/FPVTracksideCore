name = "Chase the Ace"
description = "One race of pilots. First to win twice takes the title."
author = "uewepuep"

function generate(round, pilots, channels, options)
    if not is_first_round() then
        for _, p in ipairs(pilots) do
            local wins = sum(get_results(p.id), function(r)
                return r.position == 1 and 1 or 0
            end)
            if wins >= 2 then
                return nil
            end
        end
    end

    local ids = {}
    for i = 1, math.min(#pilots, options.max_pilots_per_race) do
        table.insert(ids, pilots[i].id)
    end
    return { minimise_channel_change(ids) }
end

function standings(pilots, options)
    local scored = {}
    for _, p in ipairs(pilots) do
        local results = get_results(p.id)
        local wins   = sum(results, function(r) return r.position == 1 and 1 or 0 end)
        local points = sum(results, function(r) return r.points end)
        table.insert(scored, { id = p.id, name = p.name, wins = wins, points = points })
    end

    scored = sort_by(scored, function(s)
        return -s.wins * 1000000 - s.points
    end)

    local rows = {}
    for _, s in ipairs(scored) do
        table.insert(rows, { pilot_id = s.id, name = s.name, values = { tostring(s.wins), tostring(s.points) } })
    end

    return { headings = { "Wins", "Points" }, rows = rows }
end
