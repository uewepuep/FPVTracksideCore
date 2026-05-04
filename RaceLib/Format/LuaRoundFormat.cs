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

            RegisterHelpers(lua, pilotLookup, flownMap);

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
                Logger.AllLog.Log(this, $"Script '{scriptFile.Name}' generate() must return a table of heats.");
                return preExisting;
            }

            return BuildRaces(db, preExisting, newRound, plan, result.Table, pilotLookup, lastRoundRaces);
        }

        private void RegisterHelpers(Script lua, Dictionary<string, Pilot> pilotLookup, FlownMap flownMap)
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
            Table heatsTable, Dictionary<string, Pilot> pilotLookup, Race[] lastRoundRaces)
        {
            int heatCount = heatsTable.Length;

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
                DynValue heatDyn = heatsTable.Get(h);
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
