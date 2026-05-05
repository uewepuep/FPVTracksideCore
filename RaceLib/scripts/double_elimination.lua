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

    -- Race counts based on total pilots, not just those with results so far.
    -- This keeps the bracket structure stable as round 1 races finish.
    local winner_race_count, loser_race_count
    if is_first_round() then
        winner_race_count = math.ceil(total / max)
        loser_race_count  = 0
    else
        winner_race_count = math.ceil(math.ceil(total / 2) / max)
        loser_race_count  = math.ceil(math.floor(total / 2) / max)
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
