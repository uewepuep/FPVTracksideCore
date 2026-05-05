name = "Triple Round Elimination"
description = "3-round cycles. Bottom half of Winners drop to Losers. Bottom half of Losers are eliminated."

function generate(round, pilots, channels, options)
    local max      = options.max_per_race
    local rel      = round.number - options.stage_start_round  -- 0-based offset into stage
    local cycle    = math.floor(rel / 3)                       -- which cycle (0-indexed)
    local in_cycle = rel % 3                                   -- position within cycle (0, 1, 2)

    local winners = {}
    local losers  = {}

    if cycle == 0 then
        -- First cycle: everyone races as winners
        for _, p in ipairs(pilots) do
            table.insert(winners, p.id)
        end

    elseif in_cycle == 0 then
        -- First round of a new cycle: reassign brackets based on previous cycle points
        local cycle_start = round.number - 3
        local cycle_end   = round.number - 1

        local active = filter(pilots, function(p)
            return #get_results(p.id, cycle_start, cycle_end) > 0
        end)

        local prev_winners = filter(active, function(p)
            local b = get_bracket(p.id)
            return b == "Winners" or b == "None"
        end)
        local prev_losers = filter(active, function(p)
            return get_bracket(p.id) == "Losers"
        end)

        local function sort_by_cycle_points(group)
            return sort_by(group, function(p)
                return -sum(get_results(p.id, cycle_start, cycle_end), function(r) return r.points end)
            end)
        end

        -- Top half of winners stay, bottom half drop to losers
        local sw = sort_by_cycle_points(prev_winners)
        for i, p in ipairs(sw) do
            if i <= math.ceil(#sw / 2) then
                table.insert(winners, p.id)
            else
                table.insert(losers, p.id)
            end
        end

        -- Top half of losers stay, bottom half eliminated
        local sl = sort_by_cycle_points(prev_losers)
        for i, p in ipairs(sl) do
            if i <= math.ceil(#sl / 2) then
                table.insert(losers, p.id)
            end
        end

    else
        -- Mid-cycle: keep pilots who raced in the first round of this cycle
        local cycle_round_1 = round.number - in_cycle
        local active = filter(pilots, function(p)
            return #get_results(p.id, cycle_round_1, cycle_round_1) > 0
        end)
        for _, p in ipairs(active) do
            if get_bracket(p.id) == "Losers" then
                table.insert(losers, p.id)
            else
                table.insert(winners, p.id)
            end
        end
    end

    local races = {}

    local function distribute(group, bracket)
        if #group == 0 then return end
        local n = math.ceil(#group / max)
        local buckets = {}
        for i = 1, n do buckets[i] = {} end
        for i, id in ipairs(group) do
            table.insert(buckets[((i - 1) % n) + 1], id)
        end
        for _, bucket in ipairs(buckets) do
            table.insert(races, {
                bracket = bracket,
                pilots  = minimise_channel_change(bucket)
            })
        end
    end

    distribute(winners, "Winners")
    distribute(losers, "Losers")

    return races
end
