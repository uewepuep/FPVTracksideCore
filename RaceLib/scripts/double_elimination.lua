name = "Double Elimination"
description = "Winners bracket and Losers bracket. Lose twice and you're out."

function generate(round, pilots, channels, options)
    local max         = options.max_per_race
    local total       = #pilots
    if not is_first_round() then
        pilots = pilots_with_results(pilots)
    end

    local winners = {}
    local losers  = {}

    for _, pilot in ipairs(pilots) do
        local bracket = get_bracket(pilot.id)

        if bracket == "None" then
            table.insert(winners, pilot.id)

        elseif bracket == "Winners" then
            if top_half(pilot.id) then
                table.insert(winners, pilot.id)
            else
                table.insert(losers, pilot.id)
            end

        elseif bracket == "Losers" then
            if top_half(pilot.id) then
                table.insert(losers, pilot.id)
            end
        end
    end

    -- Grand final: one pilot left in each bracket
    if #winners == 1 and #losers == 1 then
        return {
            {
                bracket = "Winners",
                pilots  = { winners[1], losers[1] }
            }
        }
    end

    local winner_race_count, loser_race_count
    if is_first_round() then
        winner_race_count = math.ceil(total / max)
        loser_race_count  = 0
    else
        winner_race_count = math.ceil(#winners / max)
        loser_race_count  = math.ceil(#losers / max)
    end

    local races = {}

    local function distribute(pilot_ids, n_races, bracket)
        local buckets = {}
        for i = 1, n_races do buckets[i] = {} end

        local sorted = sort_by(pilot_ids, function(id)
            return get_best_consecutive_laps(id, 1)
        end)

        for i, id in ipairs(sorted) do
            table.insert(buckets[((i - 1) % n_races) + 1], id)
        end

        for _, bucket in ipairs(buckets) do
            table.insert(races, {
                bracket = bracket,
                pilots  = minimise_channel_change(bucket)
            })
        end
    end

    if winner_race_count > 0 then
        distribute(winners, winner_race_count, "Winners")
    end
    if loser_race_count > 0 then
        distribute(losers, loser_race_count, "Losers")
    end

    return races
end

function standings(pilots, options)
    local total = #pilots
    local active_winners = {}
    local active_losers  = {}
    local eliminated     = {}

    local function add_eliminated(p)
        local results = get_results(p.id)
        if #results > 0 then
            local last_round    = 0
            local last_position = 0
            for _, r in ipairs(results) do
                if r.round > last_round then
                    last_round    = r.round
                    last_position = r.position
                end
            end
            table.insert(eliminated, {
                name          = p.name,
                result_count  = #results,
                last_round    = last_round,
                last_position = last_position
            })
        end
    end

    for _, p in ipairs(pilots) do
        local bracket = get_bracket(p.id)
        if bracket == "Winners" then
            table.insert(active_winners, p)
        elseif bracket == "Losers" then
            if top_half(p.id) then
                table.insert(active_losers, p)
            else
                -- bottom half of a finished Losers race = eliminated
                add_eliminated(p)
            end
        else
            -- bracket "None" + results = eliminated in a previous round
            add_eliminated(p)
            -- bracket "None" + no results = assigned but not yet raced, skip
        end
    end

    -- Sort ascending: worst first (gets the highest final position number)
    -- Priority: fewer rounds < earlier last round < worse race finish
    -- last_position is subtracted because lower = better finish = should rank higher
    eliminated = sort_by(eliminated, function(p)
        return p.result_count * 10000 + p.last_round * 100 - p.last_position
    end)

    -- Assign positions: i=1 (worst, eliminated first) → total; i=n (best) → total - n + 1
    local n_elim = #eliminated
    for i, p in ipairs(eliminated) do
        p.position = total - i + 1
    end

    local rows = {}

    for _, p in ipairs(active_winners) do
        table.insert(rows, { name = p.name, values = { "Winners" } })
    end
    for _, p in ipairs(active_losers) do
        table.insert(rows, { name = p.name, values = { "Losers" } })
    end

    -- Show best-eliminated first (last to be knocked out nearest the top)
    for i = n_elim, 1, -1 do
        local p = eliminated[i]
        table.insert(rows, { name = p.name, values = { ordinal(p.position) } })
    end

    return { headings = { "Status" }, rows = rows }
end
