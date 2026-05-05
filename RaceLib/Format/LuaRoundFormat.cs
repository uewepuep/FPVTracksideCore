using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tools;

namespace RaceLib.Format
{
    public class LuaRoundFormat : RoundFormat
    {
        private readonly LuaFormatManager.ScriptFile scriptFile;

        public string ScriptName => scriptFile.Name;

        public LuaRoundFormat(EventManager em, Stage stage, LuaFormatManager.ScriptFile scriptFile)
            : base(em, stage)
        {
            this.scriptFile = scriptFile;
        }

        public override IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            string source;
            try
            {
                source = File.ReadAllText(scriptFile.FileInfo.FullName);
            }
            catch (Exception ex)
            {
                Logger.AllLog.LogException(this, ex);
                return preExisting;
            }

            Script lua = new Script(CoreModules.Preset_SoftSandbox);

            FlownMap flownMap = new FlownMap(RaceManager.Races);
            Race[] lastRoundRaces = RaceManager.Races.Where(r => r.Round == plan.CallingRound).ToArray();

            Pilot[] pilots = plan.Pilots;
            if (lastRoundRaces.Any())
            {
                List<Pilot> ordered = new List<Pilot>();
                IEnumerable<Race> endedRaces = lastRoundRaces.Where(r => r.Ended).OrderBy(r => r.End);
                IEnumerable<Race> unEndedRaces = lastRoundRaces.Where(r => !r.Ended).OrderBy(r => r.RaceNumber);

                ordered.AddRange(endedRaces.GetPilots());
                ordered.AddRange(unEndedRaces.GetPilots());

                pilots = ordered.ToArray();
            }

            Dictionary<string, Pilot> pilotLookup = pilots.ToDictionary(p => p.ID.ToString());
            Dictionary<string, Channel> channelLookup = plan.Channels.ToDictionary(c => c.ID.ToString());

            RegisterHelpers(lua, pilotLookup, channelLookup, flownMap, plan, lastRoundRaces);

            try
            {
                lua.DoString(source);
            }
            catch (InterpreterException ex)
            {
                Logger.AllLog.Log(this, $"Script '{scriptFile.Name}' load error: {ex.DecoratedMessage}");
                return preExisting;
            }

            DynValue generateFn = lua.Globals.Get("generate");
            if (generateFn.Type != DataType.Function)
            {
                Logger.AllLog.Log(this, $"Script '{scriptFile.Name}' does not define a generate() function.");
                return preExisting;
            }

            Table pilotTable = BuildPilotTable(lua, pilots);
            Table channelTable = BuildChannelTable(lua, plan.Channels);
            Table optionsTable = BuildOptionsTable(lua, plan);
            Table roundTable = BuildRoundTable(lua, newRound, plan);
            DynValue result;
            try
            {
                result = lua.Call(generateFn, roundTable, pilotTable, channelTable, optionsTable);
            }
            catch (InterpreterException ex)
            {
                Logger.AllLog.Log(this, $"Script '{scriptFile.Name}' generate() error: {ex.DecoratedMessage}");
                return preExisting;
            }

            if (result.Type == DataType.Nil || result.Type == DataType.Void)
                return preExisting;

            if (result.Type != DataType.Table)
            {
                Logger.AllLog.Log(this, $"Script '{scriptFile.Name}' generate() must return a table of races.");
                return preExisting;
            }

            ApplyRoundTable(roundTable, newRound);

            return BuildRaces(db, preExisting, newRound, plan, result.Table, pilotLookup, lastRoundRaces);
        }

        private void RegisterHelpers(Script lua, Dictionary<string, Pilot> pilotLookup, Dictionary<string, Channel> channelLookup, FlownMap flownMap, RoundPlan plan, Race[] lastRoundRaces)
        {
            Race[] GetRacesForRoundOffset(int roundOffset)
            {
                if (roundOffset == -1) return lastRoundRaces;
                if (plan.CallingRound == null) return Array.Empty<Race>();
                Round round = EventManager.RoundManager.GetRelativeRound(plan.CallingRound, roundOffset + 1);
                if (round == null) return Array.Empty<Race>();
                return RaceManager.GetRaces(round);
            }
            // history(pilot_id_a, pilot_id_b) -> number of times these two pilots have flown together
            lua.Globals["history"] = DynValue.NewCallback((ctx, args) =>
            {
                string idA = args[0].CastToString();
                string idB = args[1].CastToString();
                if (idA != null && idB != null &&
                    pilotLookup.TryGetValue(idA, out Pilot a) &&
                    pilotLookup.TryGetValue(idB, out Pilot b))
                {
                    return DynValue.NewNumber(flownMap.GetFlownCount(a, b));
                }
                return DynValue.NewNumber(0);
            });

            // shuffle(list) -> shuffled copy of the list
            lua.Globals["shuffle"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table)
                    return args[0];

                Table input = args[0].Table;
                List<DynValue> items = new List<DynValue>();
                for (int i = 1; i <= input.Length; i++)
                    items.Add(input.Get(i));

                Random rng = new Random();
                for (int i = items.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (items[i], items[j]) = (items[j], items[i]);
                }

                Table result = new Table(lua);
                for (int i = 0; i < items.Count; i++)
                    result[i + 1] = items[i];

                return DynValue.NewTable(result);
            });

            // get_last_channel(pilot_id[, round_offset]) -> {id, name, band} or nil
            lua.Globals["get_last_channel"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.Nil;

                Race[] races = args[1].Type == DataType.Number
                    ? GetRacesForRoundOffset((int)args[1].Number)
                    : lastRoundRaces;
                Round round = races.FirstOrDefault()?.Round ?? plan.CallingRound;

                Channel channel = pilot.GetChannelInRound(races, round);
                if (channel == null)
                    return DynValue.Nil;

                Table c = new Table(lua);
                c["id"] = channel.ID.ToString();
                c["name"] = channel.UIDisplayName;
                c["band"] = channel.Band.ToString();
                return DynValue.NewTable(c);
            });

            // top_half(pilot_id[, round_offset]) -> true if pilot finished in the top half of their race in the given round
            lua.Globals["top_half"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.False;

                Race[] races = args[1].Type == DataType.Number
                    ? GetRacesForRoundOffset((int)args[1].Number)
                    : lastRoundRaces;

                Race race = races.FirstOrDefault(r => r.HasPilot(pilot));
                if (race == null || !race.Ended)
                    return DynValue.True;

                int position = EventManager.ResultManager.GetPosition(race, pilot);
                return DynValue.NewBoolean(position > 0 && position <= race.PilotCount / 2);
            });

            // has_any_results([round_offset]) -> true if any race in the given round has ended
            lua.Globals["has_any_results"] = DynValue.NewCallback((ctx, args) =>
            {
                Race[] races = args[0].Type == DataType.Number
                    ? GetRacesForRoundOffset((int)args[0].Number)
                    : lastRoundRaces;
                return DynValue.NewBoolean(races.Any(r => r.Ended));
            });

            // is_first_round() -> true if this is the first round of the current stage
            lua.Globals["is_first_round"] = DynValue.NewCallback((ctx, args) =>
            {
                return DynValue.NewBoolean(plan.CallingRound == null || plan.CallingRound.Stage != plan.Stage);
            });

            // all_results_in([round_offset]) -> true if all races in the given round have ended
            lua.Globals["all_results_in"] = DynValue.NewCallback((ctx, args) =>
            {
                Race[] races = args[0].Type == DataType.Number
                    ? GetRacesForRoundOffset((int)args[0].Number)
                    : lastRoundRaces;
                return DynValue.NewBoolean(races.Any() && races.All(r => r.Ended));
            });

            // has_result(pilot_id[, round_offset]) -> true if the pilot has a completed race in the given round
            lua.Globals["has_result"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.False;

                Race[] races = args[1].Type == DataType.Number
                    ? GetRacesForRoundOffset((int)args[1].Number)
                    : lastRoundRaces;
                return DynValue.NewBoolean(races.Any(r => r.HasPilot(pilot) && r.Ended));
            });

            // pilots_with_results(pilots[, round_offset]) -> filtered copy of the list containing only pilots with a completed race in the given round
            lua.Globals["pilots_with_results"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table)
                    return args[0];

                Race[] races = args[1].Type == DataType.Number
                    ? GetRacesForRoundOffset((int)args[1].Number)
                    : lastRoundRaces;

                Table input = args[0].Table;
                Table result = new Table(lua);
                int outIdx = 1;
                for (int i = 1; i <= input.Length; i++)
                {
                    DynValue entry = input.Get(i);
                    string pilotId = entry.Type == DataType.Table
                        ? entry.Table.Get("id").CastToString()
                        : entry.CastToString();

                    if (pilotId != null && pilotLookup.TryGetValue(pilotId, out Pilot pilot)
                        && races.Any(r => r.HasPilot(pilot) && r.Ended))
                    {
                        result[outIdx++] = entry;
                    }
                }
                return DynValue.NewTable(result);
            });

            // get_results(pilot_id [, from_round_offset [, to_round_offset]]) -> list of result objects in the given round range
            lua.Globals["get_results"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                Table table = new Table(lua);
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewTable(table);

                Round toRound = args[2].Type == DataType.Number
                    ? EventManager.RoundManager.GetRelativeRound(plan.CallingRound, (int)args[2].Number + 1)
                    : plan.CallingRound;

                Round fromRound = args[1].Type == DataType.Number
                    ? EventManager.RoundManager.GetRelativeRound(plan.CallingRound, (int)args[1].Number + 1)
                    : null;

                int i = 1;
                foreach (Result result in EventManager.ResultManager.GetResults(toRound, pilot))
                {
                    if (fromRound != null && (result.Round?.Order ?? -1) < fromRound.Order)
                        continue;

                    Table r = new Table(lua);
                    r["points"]   = (double)result.Points;
                    r["position"] = (double)result.Position;
                    r["laps"]     = (double)result.LapsFinished;
                    r["dnf"]      = result.DNF;
                    r["round"]    = (double)(result.Round?.RoundNumber ?? 0);
                    r["time"]     = result.Time.TotalSeconds;
                    table[i++] = DynValue.NewTable(r);
                }

                return DynValue.NewTable(table);
            });

            // minimise_channel_change(race) -> reordered race
            // Reorders pilots within a race by their last channel frequency so the C# channel
            // assignment gives as many pilots as possible their previous channel.
            lua.Globals["minimise_channel_change"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table)
                    return args[0];

                Table input = args[0].Table;

                var entries = new List<(DynValue original, int freq)>();
                for (int i = 1; i <= input.Length; i++)
                {
                    DynValue entry = input.Get(i);
                    string pilotId = entry.Type == DataType.Table
                        ? entry.Table.Get("id").CastToString()
                        : entry.CastToString();

                    int freq = int.MaxValue;
                    if (pilotId != null && pilotLookup.TryGetValue(pilotId, out Pilot pilot))
                    {
                        Channel ch = pilot.GetChannelInRound(lastRoundRaces, plan.CallingRound);
                        if (ch != null) freq = ch.Frequency;
                    }
                    entries.Add((entry, freq));
                }

                entries.Sort((a, b) => a.freq.CompareTo(b.freq));

                Table result = new Table(lua);
                for (int i = 0; i < entries.Count; i++)
                    result[i + 1] = entries[i].original;

                return DynValue.NewTable(result);
            });

            // get_best_consecutive_laps(pilot_id, lap_count [, from_round_offset [, to_round_offset]]) -> best consecutive X lap time in seconds, or 0 if no data
            lua.Globals["get_best_consecutive_laps"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                int lapCount = (int)(args[1].CastToNumber() ?? 1);
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewNumber(0);

                Round fromRound = args[2].Type == DataType.Number
                    ? EventManager.RoundManager.GetRelativeRound(plan.CallingRound, (int)args[2].Number + 1)
                    : null;
                Round toRound = args[3].Type == DataType.Number
                    ? EventManager.RoundManager.GetRelativeRound(plan.CallingRound, (int)args[3].Number + 1)
                    : null;

                IEnumerable<Race> races = RaceManager.Races;
                if (fromRound != null)
                    races = races.Where(r => (r.Round?.Order ?? -1) >= fromRound.Order);
                if (toRound != null)
                    races = races.Where(r => (r.Round?.Order ?? -1) <= toRound.Order);

                IEnumerable<Lap> best = races
                    .SelectMany(r => r.GetValidLaps(pilot, false))
                    .BestConsecutive(lapCount);

                return DynValue.NewNumber(best.Any() ? best.TotalTime().TotalSeconds : 0);
            });


            // get_unflown_pilots(pilot_id) -> list of pilot objects not yet raced against
            lua.Globals["get_unflown_pilots"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                Table table = new Table(lua);
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewTable(table);

                int i = 1;
                foreach (Pilot other in flownMap.UnflownPilots(pilot, pilotLookup.Values))
                {
                    Table p = new Table(lua);
                    p["id"] = other.ID.ToString();
                    p["name"] = other.Name;
                    table[i++] = DynValue.NewTable(p);
                }
                return DynValue.NewTable(table);
            });

            // map(list, fn) -> new table with fn applied to each item
            lua.Globals["map"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table || args[1].Type != DataType.Function)
                    return args[0];

                Table input = args[0].Table;
                DynValue fn = args[1];
                Table result = new Table(lua);
                for (int i = 1; i <= input.Length; i++)
                    result[i] = lua.Call(fn, input.Get(i));
                return DynValue.NewTable(result);
            });

            // filter(list, fn) -> table of items where fn(item) is true
            lua.Globals["filter"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table || args[1].Type != DataType.Function)
                    return args[0];

                Table input = args[0].Table;
                DynValue fn = args[1];
                Table result = new Table(lua);
                int outIdx = 1;
                for (int i = 1; i <= input.Length; i++)
                {
                    DynValue item = input.Get(i);
                    if (lua.Call(fn, item).CastToBool())
                        result[outIdx++] = item;
                }
                return DynValue.NewTable(result);
            });

            // sum(list [, fn]) -> total of fn(item) for each item, or items directly if no fn
            lua.Globals["sum"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table) return DynValue.NewNumber(0);
                Table input = args[0].Table;
                DynValue fn = args[1];
                double total = 0;
                for (int i = 1; i <= input.Length; i++)
                {
                    DynValue val = fn.Type == DataType.Function ? lua.Call(fn, input.Get(i)) : input.Get(i);
                    total += val.CastToNumber() ?? 0;
                }
                return DynValue.NewNumber(total);
            });

            // average(list [, fn]) -> average of fn(item) for each item, or 0 if empty
            lua.Globals["average"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table) return DynValue.NewNumber(0);
                Table input = args[0].Table;
                if (input.Length == 0) return DynValue.NewNumber(0);
                DynValue fn = args[1];
                double total = 0;
                for (int i = 1; i <= input.Length; i++)
                {
                    DynValue val = fn.Type == DataType.Function ? lua.Call(fn, input.Get(i)) : input.Get(i);
                    total += val.CastToNumber() ?? 0;
                }
                return DynValue.NewNumber(total / input.Length);
            });

            // min(list [, fn]) -> item with the lowest fn(item) value, or lowest value if no fn
            lua.Globals["min"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table) return DynValue.Nil;
                Table input = args[0].Table;
                if (input.Length == 0) return DynValue.Nil;
                DynValue fn = args[1];
                DynValue best = input.Get(1);
                double bestVal = (fn.Type == DataType.Function ? lua.Call(fn, best) : best).CastToNumber() ?? 0;
                for (int i = 2; i <= input.Length; i++)
                {
                    DynValue item = input.Get(i);
                    double val = (fn.Type == DataType.Function ? lua.Call(fn, item) : item).CastToNumber() ?? 0;
                    if (val < bestVal) { best = item; bestVal = val; }
                }
                return best;
            });

            // max(list [, fn]) -> item with the highest fn(item) value, or highest value if no fn
            lua.Globals["max"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table) return DynValue.Nil;
                Table input = args[0].Table;
                if (input.Length == 0) return DynValue.Nil;
                DynValue fn = args[1];
                DynValue best = input.Get(1);
                double bestVal = (fn.Type == DataType.Function ? lua.Call(fn, best) : best).CastToNumber() ?? 0;
                for (int i = 2; i <= input.Length; i++)
                {
                    DynValue item = input.Get(i);
                    double val = (fn.Type == DataType.Function ? lua.Call(fn, item) : item).CastToNumber() ?? 0;
                    if (val > bestVal) { best = item; bestVal = val; }
                }
                return best;
            });

            // sort_by(list, fn) -> sorted copy of list, fn(item) returns the sort key (number or string)
            lua.Globals["sort_by"] = DynValue.NewCallback((ctx, args) =>
            {
                if (args[0].Type != DataType.Table || args[1].Type != DataType.Function)
                    return args[0];

                Table input = args[0].Table;
                DynValue fn = args[1];

                var items = new List<DynValue>();
                for (int i = 1; i <= input.Length; i++)
                    items.Add(input.Get(i));

                items.Sort((a, b) =>
                {
                    DynValue keyA = lua.Call(fn, a);
                    DynValue keyB = lua.Call(fn, b);
                    if (keyA.Type == DataType.Number && keyB.Type == DataType.Number)
                        return keyA.Number.CompareTo(keyB.Number);
                    return string.Compare(keyA.CastToString(), keyB.CastToString(), StringComparison.Ordinal);
                });

                Table result = new Table(lua);
                for (int i = 0; i < items.Count; i++)
                    result[i + 1] = items[i];
                return DynValue.NewTable(result);
            });

            // get_interfering_channels(channel_id) -> list of channel objects that interfere with this channel
            lua.Globals["get_interfering_channels"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                Table table = new Table(lua);
                if (id == null || !channelLookup.TryGetValue(id, out Channel channel))
                    return DynValue.NewTable(table);

                int i = 1;
                foreach (Channel other in channel.GetInterferringChannels(plan.Channels))
                {
                    Table c = new Table(lua);
                    c["id"] = other.ID.ToString();
                    c["name"] = other.UIDisplayName;
                    c["band"] = other.Band.ToString();
                    table[i++] = DynValue.NewTable(c);
                }
                return DynValue.NewTable(table);
            });

            // count_channel_changes(pilot_id) -> number of different channels this pilot has used across all races
            lua.Globals["count_channel_changes"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewNumber(0);

                return DynValue.NewNumber(pilot.CountChannelChanges(RaceManager.Races));
            });

            // get_bracket(pilot_id[, round_offset]) -> bracket string from the given round ("None", "Winners", "Losers", etc.)
            lua.Globals["get_bracket"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewString(Brackets.None.ToString());

                Race[] races = args[1].Type == DataType.Number
                    ? GetRacesForRoundOffset((int)args[1].Number)
                    : lastRoundRaces;
                return DynValue.NewString(races.GetBracket(pilot).ToString());
            });

            // get_pilots_in_event() -> list of all pilot objects registered in the event
            lua.Globals["get_pilots_in_event"] = DynValue.NewCallback((ctx, args) =>
            {
                Table table = new Table(lua);
                int i = 1;
                foreach (Pilot pilot in EventManager.Event.Pilots)
                {
                    Table p = new Table(lua);
                    p["id"]   = pilot.ID.ToString();
                    p["name"] = pilot.Name;
                    table[i++] = DynValue.NewTable(p);
                }
                return DynValue.NewTable(table);
            });

            // get_pilots_in_round([round_offset]) -> list of pilot objects who raced in the given round
            lua.Globals["get_pilots_in_round"] = DynValue.NewCallback((ctx, args) =>
            {
                Race[] races = args[0].Type == DataType.Number
                    ? GetRacesForRoundOffset((int)args[0].Number)
                    : lastRoundRaces;

                Table table = new Table(lua);
                int i = 1;
                foreach (Pilot pilot in races.SelectMany(r => r.Pilots).Distinct())
                {
                    Table p = new Table(lua);
                    p["id"]   = pilot.ID.ToString();
                    p["name"] = pilot.Name;
                    table[i++] = DynValue.NewTable(p);
                }
                return DynValue.NewTable(table);
            });

            // get_round_info([round_offset]) -> table with info about the given round, or nil if the round does not exist
            lua.Globals["get_round_info"] = DynValue.NewCallback((ctx, args) =>
            {
                Round round = args[0].Type == DataType.Number
                    ? EventManager.RoundManager.GetRelativeRound(plan.CallingRound, (int)args[0].Number + 1)
                    : plan.CallingRound;

                if (round == null) return DynValue.Nil;

                int stageIndex = 1;
                if (plan.Stage != null)
                {
                    Round[] stageRounds = EventManager.RoundManager.GetStageRounds(plan.Stage).ToArray();
                    int idx = Array.IndexOf(stageRounds, round);
                    stageIndex = idx >= 0 ? idx + 1 : stageRounds.Length + 1;
                }

                Table info = new Table(lua);
                info["number"]      = (double)round.RoundNumber;
                info["event_type"]  = round.EventType.ToString();
                info["name"]        = round.Name ?? "";
                info["stage_index"] = (double)stageIndex;
                return DynValue.NewTable(info);
            });
        }

        private Table BuildRoundTable(Script lua, Round round, RoundPlan plan)
        {
            Table table = new Table(lua);
            table["number"] = (double)round.RoundNumber;
            table["name"] = round.Name ?? "";
            table["event_type"] = round.EventType.ToString();
            table["game_type_name"] = round.GameTypeName ?? "";

            int stageIndex = 1;
            if (plan.Stage != null)
            {
                Round[] stageRounds = EventManager.RoundManager.GetStageRounds(plan.Stage).ToArray();
                int idx = Array.IndexOf(stageRounds, round);
                stageIndex = idx >= 0 ? idx + 1 : stageRounds.Length + 1;
            }
            table["stage_index"] = (double)stageIndex;

            return table;
        }

        private void ApplyRoundTable(Table table, Round round)
        {
            DynValue name = table.Get("name");
            if (name.Type == DataType.String)
                round.Name = name.String;

            DynValue eventType = table.Get("event_type");
            if (eventType.Type == DataType.String && Enum.TryParse(eventType.String, true, out EventTypes et))
                round.EventType = et;

            DynValue gameTypeName = table.Get("game_type_name");
            if (gameTypeName.Type == DataType.String)
                round.GameTypeName = gameTypeName.String;
        }

        private Table BuildPilotTable(Script lua, Pilot[] pilots)
        {
            Table table = new Table(lua);
            for (int i = 0; i < pilots.Length; i++)
            {
                Table p = new Table(lua);
                p["id"] = pilots[i].ID.ToString();
                p["name"] = pilots[i].Name;
                table[i + 1] = DynValue.NewTable(p);
            }
            return table;
        }

        private Table BuildChannelTable(Script lua, Channel[] channels)
        {
            Table table = new Table(lua);
            for (int i = 0; i < channels.Length; i++)
            {
                Table c = new Table(lua);
                c["id"] = channels[i].ID.ToString();
                c["name"] = channels[i].UIDisplayName;
                c["band"] = channels[i].Band.ToString();
                table[i + 1] = DynValue.NewTable(c);
            }
            return table;
        }

        private Table BuildOptionsTable(Script lua, RoundPlan plan)
        {
            Table table = new Table(lua);
            table["race_count"] = (double)plan.NumberOfRaces;
            table["max_per_race"] = (double)plan.Channels.Length;
            table["target_laps"] = (double)EventManager.Event.Laps;
            table["pb_laps"] = (double)EventManager.Event.PBLaps;

            return table;
        }

        private IEnumerable<Race> BuildRaces(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan,
            Table racesTable, Dictionary<string, Pilot> pilotLookup, Race[] lastRoundRaces)
        {
            int raceCount = racesTable.Length;

            List<Race> races = new List<Race>();
            races.AddRange(preExisting.OrderBy(r => r.RaceOrder));

            while (races.Count < raceCount)
                races.Add(new Race(EventManager.Event));
            while (races.Count > raceCount)
                races.Remove(races.Last());

            for (int i = 0; i < races.Count; i++)
            {
                races[i].RaceNumber = i + 1;
                races[i].Round = newRound;
            }

            for (int h = 1; h <= raceCount; h++)
            {
                DynValue raceDyn = racesTable.Get(h);
                if (raceDyn.Type != DataType.Table) continue;

                Race race = races[h - 1];
                Table raceTable = raceDyn.Table;

                // Read optional bracket
                DynValue bracketDyn = raceTable.Get("bracket");
                if (bracketDyn.Type == DataType.String && Enum.TryParse(bracketDyn.String, true, out Brackets bracket))
                    race.Bracket = bracket;

                // Read optional target_laps
                DynValue lapsDyn = raceTable.Get("target_laps");
                if (lapsDyn.Type == DataType.Number)
                    race.TargetLaps = (int)lapsDyn.Number;

                // Pilots are either in a "pilots" sub-table or in the array part of the race table
                DynValue pilotsDyn = raceTable.Get("pilots");
                Table pilotList = pilotsDyn.Type == DataType.Table ? pilotsDyn.Table : raceTable;

                Pilot[] existingPilots = race.Pilots;
                List<Pilot> assignedPilots = new List<Pilot>();

                for (int p = 1; p <= pilotList.Length; p++)
                {
                    string pilotId = pilotList.Get(p).CastToString();
                    if (pilotId == null || !pilotLookup.TryGetValue(pilotId, out Pilot pilot))
                    {
                        continue;
                    }

                    BandType bandType = BandType.Analogue;
                    Channel prevChannel = pilot.GetChannelInRound(lastRoundRaces, plan.CallingRound);
                    if (prevChannel != null)
                        bandType = prevChannel.Band.GetBandType();

                    Channel channel = prevChannel != null && race.IsFrequencyFree(prevChannel) && plan.Channels.Contains(prevChannel)
                        ? prevChannel
                        : plan.Channels.Where(c => race.IsFrequencyFree(c) && c.Band.GetBandType() == bandType).FirstOrDefault()
                          ?? plan.Channels.FirstOrDefault(c => race.IsFrequencyFree(c));

                    if (channel != null)
                    {
                        race.SetPilot(db, channel, pilot);
                        assignedPilots.Add(pilot);
                    }
                }

                foreach (Pilot toRemove in existingPilots)
                {
                    if (!assignedPilots.Contains(toRemove))
                    {
                        race.RemovePilot(db, toRemove);
                    }
                }
            }

            return races;
        }
    }
}
