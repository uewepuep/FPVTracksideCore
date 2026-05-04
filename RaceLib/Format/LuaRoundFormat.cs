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
            Dictionary<string, Pilot> pilotLookup = plan.Pilots.ToDictionary(p => p.ID.ToString());
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

            Table pilotTable = BuildPilotTable(lua, plan.Pilots);
            Table channelTable = BuildChannelTable(lua, plan.Channels);
            Table optionsTable = BuildOptionsTable(lua, plan);

            DynValue result;
            try
            {
                result = lua.Call(generateFn, pilotTable, channelTable, optionsTable);
            }
            catch (InterpreterException ex)
            {
                Logger.AllLog.Log(this, $"Script '{scriptFile.Name}' generate() error: {ex.DecoratedMessage}");
                return preExisting;
            }

            if (result.Type != DataType.Table)
            {
                Logger.AllLog.Log(this, $"Script '{scriptFile.Name}' generate() must return a table of races.");
                return preExisting;
            }

            return BuildRaces(db, preExisting, newRound, plan, result.Table, pilotLookup, lastRoundRaces);
        }

        private void RegisterHelpers(Script lua, Dictionary<string, Pilot> pilotLookup, Dictionary<string, Channel> channelLookup, FlownMap flownMap, RoundPlan plan, Race[] lastRoundRaces)
        {
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

            // get_last_channel(pilot_id) -> {id, name, band} or nil
            lua.Globals["get_last_channel"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.Nil;

                Channel channel = pilot.GetChannelInRound(lastRoundRaces, plan.CallingRound);
                if (channel == null)
                    return DynValue.Nil;

                Table c = new Table(lua);
                c["id"] = channel.ID.ToString();
                c["name"] = channel.UIDisplayName;
                c["band"] = channel.Band.ToString();
                return DynValue.NewTable(c);
            });

            // top_half(pilot_id) -> true if pilot finished in the top half of their heat last round
            lua.Globals["top_half"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.False;

                Race race = lastRoundRaces.FirstOrDefault(r => r.HasPilot(pilot));
                if (race == null)
                    return DynValue.False;

                int position = EventManager.ResultManager.GetPosition(race, pilot);
                return DynValue.NewBoolean(position > 0 && position <= race.PilotCount / 2);
            });

            // get_points(pilot_id) -> total points up to and including the calling round
            lua.Globals["get_points"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewNumber(0);

                int points = EventManager.ResultManager.GetPointsTotal(plan.CallingRound, pilot);
                return DynValue.NewNumber(points);
            });

            // get_positions(pilot_id) -> list of positions, one per result up to the calling round
            lua.Globals["get_positions"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                Table table = new Table(lua);
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewTable(table);

                int i = 1;
                foreach (Result result in EventManager.ResultManager.GetResults(plan.CallingRound, pilot))
                    table[i++] = DynValue.NewNumber(result.Position);

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

            // get_best_consecutive_laps_stage(pilot_id, lap_count) -> best consecutive X lap time in seconds within the current stage, or 0 if no data
            lua.Globals["get_best_consecutive_laps_stage"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                int lapCount = (int)(args[1].CastToNumber() ?? 1);
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewNumber(0);

                Stage stage = plan.CallingRound?.Stage;
                IEnumerable<Race> stageRaces = stage != null
                    ? RaceManager.Races.Where(r => r.Round?.Stage == stage)
                    : Enumerable.Empty<Race>();

                IEnumerable<Lap> best = stageRaces
                    .SelectMany(r => r.GetValidLaps(pilot, false))
                    .BestConsecutive(lapCount);

                return DynValue.NewNumber(best.Any() ? best.TotalTime().TotalSeconds : 0);
            });

            // get_best_consecutive_laps_event(pilot_id, lap_count) -> best consecutive X lap time in seconds across the whole event, or 0 if no data
            lua.Globals["get_best_consecutive_laps_event"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                int lapCount = (int)(args[1].CastToNumber() ?? 1);
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewNumber(0);

                IEnumerable<Lap> best = RaceManager.Races
                    .SelectMany(r => r.GetValidLaps(pilot, false))
                    .BestConsecutive(lapCount);

                return DynValue.NewNumber(best.Any() ? best.TotalTime().TotalSeconds : 0);
            });

            // get_laps_finished(pilot_id) -> number of laps the pilot completed in their last race
            lua.Globals["get_laps_finished"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewNumber(0);

                Race race = lastRoundRaces.FirstOrDefault(r => r.HasPilot(pilot));
                if (race == null)
                    return DynValue.NewNumber(0);

                Result result = EventManager.ResultManager.GetResult(race, pilot);
                return DynValue.NewNumber(result?.LapsFinished ?? 0);
            });

            // get_unflown_pilots(pilot_id) -> list of pilot objects not yet raced against
            lua.Globals["get_unflown_pilots"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                Table table = new Table(lua);
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewTable(table);

                int i = 1;
                foreach (Pilot other in flownMap.UnflownPilots(pilot, plan.Pilots))
                {
                    Table p = new Table(lua);
                    p["id"] = other.ID.ToString();
                    p["name"] = other.Name;
                    table[i++] = DynValue.NewTable(p);
                }
                return DynValue.NewTable(table);
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

            // get_bracket(pilot_id) -> bracket string from the calling round ("None", "Winners", "Losers", etc.)
            lua.Globals["get_bracket"] = DynValue.NewCallback((ctx, args) =>
            {
                string id = args[0].CastToString();
                if (id == null || !pilotLookup.TryGetValue(id, out Pilot pilot))
                    return DynValue.NewString(Brackets.None.ToString());

                return DynValue.NewString(lastRoundRaces.GetBracket(pilot).ToString());
            });
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
            table["heat_count"] = (double)plan.NumberOfRaces;
            table["max_per_heat"] = (double)plan.Channels.Length;
            return table;
        }

        private IEnumerable<Race> BuildRaces(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan,
            Table racesTable, Dictionary<string, Pilot> pilotLookup, Race[] lastRoundRaces)
        {
            int heatCount = racesTable.Length;

            List<Race> races = new List<Race>();
            races.AddRange(preExisting.OrderBy(r => r.RaceOrder));

            while (races.Count < heatCount)
                races.Add(new Race(EventManager.Event));
            while (races.Count > heatCount)
                races.Remove(races.Last());

            for (int i = 0; i < races.Count; i++)
            {
                races[i].RaceNumber = i + 1;
                races[i].Round = newRound;
            }

            for (int h = 1; h <= heatCount; h++)
            {
                DynValue heatDyn = racesTable.Get(h);
                if (heatDyn.Type != DataType.Table) continue;

                Race race = races[h - 1];
                Table heatPilots = heatDyn.Table;

                for (int p = 1; p <= heatPilots.Length; p++)
                {
                    string pilotId = heatPilots.Get(p).CastToString();
                    if (pilotId == null || !pilotLookup.TryGetValue(pilotId, out Pilot pilot))
                        continue;

                    BandType bandType = BandType.Analogue;
                    Channel prevChannel = pilot.GetChannelInRound(lastRoundRaces, plan.CallingRound);
                    if (prevChannel != null)
                        bandType = prevChannel.Band.GetBandType();

                    Channel channel = prevChannel != null && race.IsFrequencyFree(prevChannel) && plan.Channels.Contains(prevChannel)
                        ? prevChannel
                        : plan.Channels.Where(c => race.IsFrequencyFree(c) && c.Band.GetBandType() == bandType).FirstOrDefault()
                          ?? plan.Channels.FirstOrDefault(c => race.IsFrequencyFree(c));

                    if (channel != null)
                        race.SetPilot(db, channel, pilot);
                }
            }

            return races;
        }
    }
}
