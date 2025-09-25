using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spreadsheets;
using Tools;

namespace RaceLib.Format
{
    public class SheetFormatManager : IDisposable
    {
        public RoundManager RoundManager { get; private set; }
        public EventManager EventManager { get; private set; }
        public RaceManager RaceManager { get; private set; }

        private List<RoundSheetFormat> roundSheetFormats;

        private List<SheetFile> sheets;

        public IEnumerable<SheetFile> Sheets
        {
            get
            {
                return sheets;
            }
        }

        private RoundSheetFormat[] roundSheetFormatsSafe
        {
            get
            {
                lock (roundSheetFormats)
                {
                    return roundSheetFormats.ToArray();
                }
            }
        }

        public SheetFormatManager(RoundManager roundManager)
            :this(roundManager, new DirectoryInfo("formats"))
        {
        }

        public SheetFormatManager(RoundManager roundManager, DirectoryInfo directory)
        {
            RoundManager = roundManager;
            EventManager = roundManager.EventManager;
            RaceManager = EventManager.RaceManager;

            EventManager.OnPilotRefresh += EventManager_OnPilot;

            roundSheetFormats = new List<RoundSheetFormat>();

            sheets = GetSheetFiles(directory).ToList();
        }

        public bool CanAddFormat(Round round)
        {
            if (!sheets.Any())
                return false;

            Round next = RoundManager.NextRound(round);
            if (next != null && next.Stage != null)
            {
                if (next.Stage.HasSheetFormat)
                    return false;

                if (RaceManager.GetRaces(next).Any())
                    return false;
            }

            return true;
        }

        public void Clear()
        {
            lock (roundSheetFormats)
            {
                foreach (var format in roundSheetFormats)
                {
                    format.Dispose();
                }
                roundSheetFormats.Clear();
            }
        }

        public void Load()
        {
            List<Stage> alreadyLoaded = new List<Stage>();

            foreach (Round r in RoundManager.RoundsWhere(r => r.Stage != null).OrderBy(r => r.Order))
            {
                if (alreadyLoaded.Contains(r.Stage))
                    continue;

                alreadyLoaded.Add(r.Stage);

                if (r.Stage.HasSheetFormat)
                {
                    LoadSheet(r, null, false);
                }
            }
        }

        public void Dispose()
        {
            EventManager.OnPilotRefresh -= EventManager_OnPilot;
            Clear();
        }

        private IEnumerable<SheetFile> GetSheetFiles(DirectoryInfo directory)
        {
            if (directory.Exists)
            {
                foreach (FileInfo fileInfo in directory.GetFiles("*.xlsx"))
                {
                    if (fileInfo.Name.StartsWith("~"))
                        continue;

                    SheetFile sheetFile = null;
                    try
                    {
                        sheetFile = new SheetFile(fileInfo);
                    }
                    catch (Exception e)
                    {
                        Logger.AllLog.LogException(this, e);
                    }

                    if (sheetFile != null)
                    {
                        yield return sheetFile;
                    }
                }
            }
        }

        private void EventManager_OnPilot()
        {
            var formats = roundSheetFormatsSafe;
            foreach (var format in formats)
            {
                format.CreatePilotMap(null);
            }
        }


        public SheetFile GetSheetFile(string filename)
        {
            SheetFile sheetFile = sheets.FirstOrDefault(r => r.FileInfo.Name == filename);
            if (sheetFile != null)
            {
                if (sheetFile.FileInfo.Exists)
                {
                    return sheetFile;
                }
            }

            return null;
        }

        public RoundSheetFormat GetRoundSheetFormat(Round round)
        {
            lock (roundSheetFormats)
            {
                return roundSheetFormats.FirstOrDefault(r => r.HasRound(round));
            }
        }


        public void LoadSheet(Round roundInStage, Pilot[] assignedPilots, bool generate)
        {
            Stage stage = roundInStage.Stage;
            if (stage.HasSheetFormat)
            {
                SheetFile sheetFile = GetSheetFile(stage.SheetFormatFilename);
                RoundSheetFormat sheetFormat = new RoundSheetFormat(roundInStage, this, sheetFile.FileInfo);
                sheetFormat.CreatePilotMap(assignedPilots);

                foreach (Round round in sheetFormat.Rounds)
                {
                    IEnumerable<Race> races = RaceManager.GetRaces(r => r.Round == round);
                    foreach (Race race in races.Where(r => r.Ended))
                    {
                        sheetFormat.SetResult(race);
                    }

                    foreach (Race race in races)
                    {
                        sheetFormat.SyncRace(race);
                    }
                }

                if (generate)
                {
                    sheetFormat.GenerateRounds();
                }

                lock (roundSheetFormats)
                {
                    roundSheetFormats.Add(sheetFormat);
                }
            }
        }

        public void OnRaceResultChange(Race race)
        {
            var formats = roundSheetFormatsSafe;
            foreach (var format in formats)
            {
                if (format.HasRound(race.Round))
                {
                    format.SetResult(race);
                    format.GenerateRounds();
                }
            }
        }

        public class SheetFile
        {
            public int Channels { get; private set; }
            public int Pilots { get; private set; }
            public string Name { get; private set; }

            public FileInfo FileInfo { get; private set; }

            public SheetFile(FileInfo fileInfo)
            {
                FileInfo = fileInfo;
                
                SheetFormat sheetFormat = new SheetFormat(FileInfo);
                Pilots = sheetFormat.GetPilots().Count();
                Channels = sheetFormat.Channels;
                Name = sheetFormat.Name;
            }
        }
    }

    public class RoundSheetFormat : IDisposable
    {
        private Dictionary<string, Pilot> pilotMap;
        
        public SheetFormatManager SheetFormatManager { get; private set; }

        public Round StartRound { get; private set; }
        public List<Round> Rounds { get; private set; }
        public SheetFormat SheetFormat { get; private set; }

        public Action OnGenerate;

        public string Name
        {
            get
            {
                return SheetFormat.Name;
            }
        }

        public int Offset
        {
            get
            {
                if (StartRound == null)
                    return 0;

                return StartRound.RoundNumber - 1;
            }
        }

        public int ChannelCount
        {
            get
            {
                if (SheetFormat != null)
                {
                    return SheetFormat.Channels;
                }
                return 0;
            }
        }

        public RoundSheetFormat(Round startRound, SheetFormatManager sheetFormatManager, FileInfo file)
        {
            SheetFormatManager = sheetFormatManager;
            StartRound = startRound;
            Rounds = new List<Round>();
            SheetFormat = new SheetFormat(file);
            pilotMap = new Dictionary<string, Pilot>();
        }
        public void Dispose()
        {
            SheetFormat.Dispose();
        }

        public bool HasRound(Round round)
        {
            lock (Rounds)
            {
                return Rounds.Contains(round);
            }
        }

        public void CreatePilotMap(Pilot[] assignedPilots)
        {
            pilotMap.Clear();

            string[] sheetPilots = null;

            Round round = GetCreateRounds().FirstOrDefault();

            // If we don't have assigned pilots, we're probably continuing...
            if (assignedPilots == null || !assignedPilots.Any())
            {
                assignedPilots = SheetFormatManager.RaceManager.GetRaces(round).OrderBy(r => r.RaceNumber).SelectMany(r => r.Pilots).ToArray();
                sheetPilots = SheetFormat.GetFirstRoundPilots().ToArray();
            }
            else
            { 
                sheetPilots = SheetFormat.GetPilots().ToArray();
            }

            if (assignedPilots == null || !assignedPilots.Any())
            {
                assignedPilots = SheetFormatManager.EventManager.Event.Pilots.ToArray();
            }

            int length = Math.Min(assignedPilots.Length, sheetPilots.Length);

            int i;
            for (i = 0; i < length; i++)
            {
                if (!pilotMap.ContainsKey(sheetPilots[i]))
                {
                    pilotMap.Add(sheetPilots[i], assignedPilots[i]);
                }
            }

            for (; i < sheetPilots.Length; i++)
            {
                if (!pilotMap.ContainsKey(sheetPilots[i]))
                {
                    Pilot pilot = SheetFormatManager.EventManager.Event.Pilots.Where(p => !pilotMap.ContainsValue(p)).FirstOrDefault();
                    if (pilot != null)
                    {
                        pilotMap.Add(sheetPilots[i], pilot);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        public bool GetPilotMapped(string text, out Pilot pilot)
        {
            return pilotMap.TryGetValue(text, out pilot);
        }

        public IEnumerable<Round> GetCreateRounds()
        {
            string[] rounds = SheetFormat.GetRounds().ToArray();

            lock (Rounds)
            {
                Rounds.Clear();

                foreach (string round in rounds)
                {
                    Round r = GetCreateRound(round);
                    if (r != null)
                    {
                        Rounds.Add(r);
                    }
                }
                return Rounds.ToArray();
            }

        }

        public Round GetCreateRound(string name)
        {
            string[] splits = name.Split(' ');

            EventTypes eventType = EventTypes.Race;
            int round = -1;

            foreach (string s in splits)
            {
                EventTypes t = GetEventType(s);
                if (t >= 0)
                {
                    eventType = t;
                }
                int i;
                if (int.TryParse(s, out i))
                {
                    round = i;
                }
            }

            if (round > 0)
            {
                if (round == 1 && StartRound.EventType != eventType)
                {
                    using (IDatabase db = DatabaseFactory.Open(SheetFormatManager.EventManager.EventId))
                    {
                        StartRound.EventType = eventType;
                        db.Update(StartRound);
                    }
                }

                return SheetFormatManager.RoundManager.GetCreateRound(round + Offset, eventType);
            }

            return null;
        }

        public void GenerateRounds()
        {
            Round[] rounds = GetCreateRounds().ToArray();

            foreach (Round round in rounds)
            {
                var races = SheetFormatManager.RaceManager.GetRaces(round);
                if (!races.All(r => r.Ended) || !races.Any())
                {
                    GenerateRound(round);
                }
            }
            OnGenerate?.Invoke();
        }

        public void GenerateSingleRound(Round round)
        {
            GenerateRound(round);
            OnGenerate?.Invoke();
        }

        private void GenerateRound(Round round)
        {
            int count = 0;

            var brackets = Enum.GetValues(typeof(Brackets)).OfType<Brackets>().Where(e => e >= Brackets.A && e <= Brackets.Z).ToArray();

            SheetRace[] sfRaces = SheetFormat.GetRaces(round.EventType.ToString(), round.RoundNumber - Offset).ToArray();
            foreach (SheetRace sfRace in sfRaces)
            {
                Race race = GetCreateRace(round, sfRace);
                if (SheetFormat.CreateBrackets && count < brackets.Length)
                {
                    race.Bracket = brackets[count];
                }
                count++;
            }
        }

        private Race GetCreateRace(Round round, SheetRace srace)
        {
            Race race = SheetFormatManager.RaceManager.GetCreateRace(round, srace.Number);
            if (race.Started)
            {
                return race;
            }

            using (IDatabase db = DatabaseFactory.Open(SheetFormatManager.EventManager.EventId))
            {
                race.ClearPilots(db);

                foreach (var pc in srace.PilotChannels)
                {
                    IEnumerable<Channel> channels = SheetFormatManager.EventManager.GetChannelGroup(pc.ChannelSlot);
                    Pilot pilot = GetPilot(pc.PilotSheetName);
                    if (pilot != null && channels.Any())
                    {
                        Channel currentChannel = SheetFormatManager.EventManager.GetChannel(pilot);
                        if (currentChannel != null)
                        {
                            Channel channel = channels.FirstOrDefault(r => r.Band.GetBandType() == currentChannel.Band.GetBandType());
                            if (channel == null)
                            {
                                channel = channels.FirstOrDefault();
                            }

                            if (channel != null)
                            {
                                race.SetPilot(db, channel, pilot);
                            }
                        }
                    }
                }

                if (!SheetFormat.LockChannels)
                {
                    SheetFormatManager.EventManager.RaceManager.OptimiseChannels(db, race);
                }
            }

            return race;
        }

        public void SetResult(Race race)
        {
            List<SheetResult> sheetResults = new List<SheetResult>();

            Result[] orderedResults = SheetFormatManager.EventManager.ResultManager.GetOrderedResults(race).ToArray();

            // Calcualte position rather than using results as this does DNF better.
            int position = 1;
            foreach (Result result in orderedResults)
            {
                Channel channel = race.GetChannel(result.Pilot);
                if (channel == null)
                    continue;

                int channelGroup = SheetFormatManager.EventManager.GetChannelGroupIndex(channel);
                string pilotSheetName = GetPilotSheetName(result.Pilot);

                if (!string.IsNullOrEmpty(pilotSheetName))
                {
                    SheetResult sr = new SheetResult(pilotSheetName, result.Pilot.Name, channelGroup, position);
                    sheetResults.Add(sr);
                    position++;
                }
            }
            SheetFormat.SetResults(race.Round.EventType.ToString(), race.RoundNumber - Offset, race.RaceNumber, sheetResults);
        }

        private Pilot GetPilot(string pilotRef)
        {
            Pilot p;
            if (pilotMap.TryGetValue(pilotRef, out p))
            {
                return p;
            }
            return null;
        }

        private string GetPilotSheetName(Pilot pilot)
        {
            foreach (var kvp in pilotMap)
            {
                if (pilot == kvp.Value)
                {
                    return kvp.Key;
                }
            }
            return "";
        }

        public EventTypes GetEventType(string eventtypestring)
        {
            foreach (EventTypes eventType in Enum.GetValues(typeof(EventTypes)))
            {
                if (eventType.ToString().ToLower() == eventtypestring.ToLower())
                {
                    return eventType;
                }
            }

            return (EventTypes)(-1);
        }
        public void Save(string fileName)
        {
            IEnumerable<string> eventTypes = Enum.GetValues(typeof(EventTypes)).OfType<EventTypes>().Select(r => r.ToString());

            Dictionary<string, string> pilotNameMap = pilotMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);
            SheetFormat.Save(fileName, pilotNameMap, eventTypes);
        }

        public void SwapPilots(Race race, Pilot oldPilot, Pilot newPilot)
        {
            string oldPilotSheetName = GetPilotSheetName(oldPilot);
            string newPilotSheetName = GetPilotSheetName(newPilot);

            if (!string.IsNullOrEmpty(oldPilotSheetName) && !string.IsNullOrEmpty(newPilotSheetName))
            {
                SheetFormat.SwapPilots(race.Round.EventType.ToString(), race.RoundNumber - Offset, race.RaceNumber, oldPilotSheetName, newPilotSheetName);
            }
        }

        public void SyncRace(Race race)
        {
            if (SheetFormat == null)
                return;

            Round round = race.Round;

            string eventType = race.Round.EventType.ToString();
            int roundNumber = race.RoundNumber - Offset;

            // Get the specifc race.
            SheetRace sfRace = SheetFormat.GetRaces(round.EventType.ToString(), round.RoundNumber - Offset).FirstOrDefault(r => r.Round == round.RoundNumber && r.Number == race.RaceNumber);
            if (sfRace != null) 
            {
                string[] newPilotSheetNames = race.Pilots.Select(r => GetPilotSheetName(r)).ToArray();
                string[] oldPilotSheetNames = sfRace.PilotChannels.Select(r => r.PilotSheetName).ToArray();

                // foreach pilot that exists in the sheet, but not in the race.
                foreach (string name in oldPilotSheetNames.Except(newPilotSheetNames))
                {
                    SheetFormat.SwapPilots(eventType, roundNumber, race.RaceNumber, name, "");
                }

                // foreach pilot that doesn't exist in the sheet but does in the race
                foreach (string name in newPilotSheetNames.Except(oldPilotSheetNames))
                {
                    SheetFormat.SwapPilots(eventType, roundNumber, race.RaceNumber, "", name);
                }
            }
        }
    }
}
