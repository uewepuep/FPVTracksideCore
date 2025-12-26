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
                    LoadSheet(r.Stage, null, false);
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


        public void LoadSheet(Stage stage, Pilot[] assignedPilots, bool generate)
        {
            if (stage != null && stage.HasSheetFormat)
            {
                Round[] stageRounds = EventManager.RoundManager.GetStageRounds(stage).ToArray();

                int offset = 0;
                if (stageRounds.Any())
                {
                    offset = stageRounds.OrderBy(r => r.Order).FirstOrDefault().RoundNumber - 1;
                }
                else
                {
                    Round last = EventManager.RoundManager.GetLastRound(EventTypes.Race);
                    if (last != null)
                    {
                        offset = last.RoundNumber;
                    }
                    else
                    {
                        // No existing rounds: do not offset sheet round numbers.
                        offset = 0;
                    }
                }

                SheetFile sheetFile = GetSheetFile(stage.SheetFormatFilename);
                RoundSheetFormat sheetFormat = new RoundSheetFormat(stage, this, sheetFile.FileInfo, offset);
                sheetFormat.CreatePilotMap(assignedPilots);

                foreach (Round round in sheetFormat.Rounds)
                {
                    IEnumerable<Race> races = RaceManager.GetRaces(round);
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

        public Stage Stage { get; private set; }
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

        public int Offset { get; private set; }

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

        public RoundSheetFormat(Stage stage, SheetFormatManager sheetFormatManager, FileInfo file, int offset)
        {
            Offset = offset;
            SheetFormatManager = sheetFormatManager;
            Stage = stage;
            Rounds = new List<Round>();
            SheetFormat = new SheetFormat(file);
            pilotMap = new Dictionary<string, Pilot>();
        }

        public void Dispose()
        {
            SheetFormat.Dispose();
        }

        private Round GetFirstRound()
        {
            return SheetFormatManager.RoundManager.GetStageRounds(Stage).FirstOrDefault();
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
            bool usingFirstRoundSlots = false;

            Round round = GetCreateRounds().FirstOrDefault();

            // If we don't have assigned pilots, we're probably generating from first-round sheet slots.
            if (assignedPilots == null || !assignedPilots.Any())
            {
                assignedPilots = SheetFormatManager.RaceManager
                    .GetRaces(round)
                    .OrderBy(r => r.RaceNumber)
                    .SelectMany(r => r.Pilots)
                    .ToArray();

                sheetPilots = SheetFormat.GetFirstRoundPilots().ToArray();
                usingFirstRoundSlots = true;
            }
            else
            {
                sheetPilots = SheetFormat.GetPilots().ToArray();
                usingFirstRoundSlots = false;
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

            // Fill remaining sheet slots:
            // 1) Prefer any unused real pilots from the event.
            // 2) If still short AND we are filling first-round slots, create fake pilots to exactly fill the deficit.
            char baseLetter = 'A';
            for (; i < sheetPilots.Length; i++)
            {
                if (pilotMap.ContainsKey(sheetPilots[i]))
                    continue;

                Pilot nextReal = SheetFormatManager.EventManager.Event.Pilots
                    .Where(p => !pilotMap.ContainsValue(p)).FirstOrDefault();

                if (nextReal != null)
                {
                    pilotMap.Add(sheetPilots[i], nextReal);
                    continue;
                }

                // Only create fake pilots for the first round
                if (!usingFirstRoundSlots)
                {
                    // Do not create fake pilots for non-first rounds.
                    break;
                }

                int missingIndex = i - length; // 0-based within missing region
                char letter = (char)(baseLetter + (missingIndex % 26));
                int repeat = missingIndex / 26;
                string suffix = repeat == 0 ? "" : (repeat + 1).ToString();
                string fakeName = $"{letter} missing pilot{suffix}";

                Pilot fakePilot = null;
                try
                {
                    fakePilot = new Pilot() { Name = fakeName };

                    // DO NOT add fake pilots to the global Event.Pilots collection.
                    // Keeping them local prevents placeholders leaking into later rounds.
                    // This avoids persisted fake pilots and unexpected side-effects.
                }
                catch
                {
                    fakePilot = null;
                }

                if (fakePilot != null)
                {
                    pilotMap.Add(sheetPilots[i], fakePilot);
                }
                else
                {
                    // Cannot create more pilots - stop attempting to fill further
                    break;
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
                Round first = GetFirstRound();

                if (first != null && round == 1 && first.EventType != eventType)
                {
                    using (IDatabase db = DatabaseFactory.Open(SheetFormatManager.EventManager.EventId))
                    {
                        first.EventType = eventType;
                        db.Update(first);
                    }
                }

                return SheetFormatManager.RoundManager.GetCreateRound(round + Offset, eventType, Stage);
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

            // Build tentative assignments while preferring pilots' last channels.
            // On duplicates we will move the higher-ranked pilot (lower index = higher rank) to an available candidate channel.
            var assigned = new Dictionary<Channel, (Pilot pilot, int rankIndex, Channel[] candidates)>();
            var pilotsProcessed = new HashSet<Pilot>();

            Channel[] GetCandidates(int channelSlot)
            {
                return SheetFormatManager.EventManager.GetChannelGroup(channelSlot)?.ToArray() ?? Array.Empty<Channel>();
            }

            var pilotChannels = srace.PilotChannels ?? new SheetPilotChannel[0];

            for (int idx = 0; idx < pilotChannels.Length; idx++)
            {
                var pc = pilotChannels[idx];
                Pilot pilot = GetPilot(pc.PilotSheetName);
                if (pilot == null)
                    continue;

                // prevent assigning same pilot twice
                if (pilotsProcessed.Contains(pilot))
                    continue;
                pilotsProcessed.Add(pilot);

                Channel currentChannel = SheetFormatManager.EventManager.GetChannel(pilot);
                Channel[] candidates = GetCandidates(pc.ChannelSlot);
                if (!candidates.Any())
                    continue;

                // preferred channel: try to match band type of current channel first
                Channel preferred = null;
                if (currentChannel != null)
                {
                    preferred = candidates.FirstOrDefault(c => c.Band.GetBandType() == currentChannel.Band.GetBandType());
                }
                if (preferred == null)
                {
                    preferred = candidates.FirstOrDefault();
                }
                if (preferred == null)
                    continue;

                if (!assigned.ContainsKey(preferred))
                {
                    assigned[preferred] = (pilot, idx, candidates);
                    continue;
                }

                var existing = assigned[preferred];

                // Determine which pilot is higher ranked (lower idx)
                bool currentIsHigherRank = idx < existing.rankIndex;
                // Per request: move the higher-ranked pilot when duplicates occur
                if (currentIsHigherRank)
                {
                    // Attempt to move current pilot to another available candidate
                    bool movedCurrent = false;
                    foreach (var alt in candidates)
                    {
                        if (alt == preferred) continue;
                        if (!assigned.ContainsKey(alt))
                        {
                            assigned[alt] = (pilot, idx, candidates);
                            movedCurrent = true;
                            break;
                        }
                    }
                    if (!movedCurrent)
                    {
                        // couldn't move current; try to move existing instead (as fallback)
                        bool movedExisting = false;
                        foreach (var alt in existing.candidates)
                        {
                            if (alt == preferred) continue;
                            if (!assigned.ContainsKey(alt))
                            {
                                assigned.Remove(preferred);
                                assigned[alt] = (existing.pilot, existing.rankIndex, existing.candidates);
                                assigned[preferred] = (pilot, idx, candidates);
                                movedExisting = true;
                                break;
                            }
                        }
                        if (!movedExisting)
                        {
                            // neither can be moved - keep existing as-is and skip assigning current
                            // (this means current gets no channel in this pass)
                        }
                    }
                }
                else
                {
                    // existing pilot is higher ranked -> move existing pilot to an alternative if possible
                    bool movedExisting = false;
                    foreach (var alt in existing.candidates)
                    {
                        if (alt == preferred) continue;
                        if (!assigned.ContainsKey(alt))
                        {
                            assigned.Remove(preferred);
                            assigned[alt] = (existing.pilot, existing.rankIndex, existing.candidates);
                            assigned[preferred] = (pilot, idx, candidates);
                            movedExisting = true;
                            break;
                        }
                    }
                    if (!movedExisting)
                    {
                        // couldn't move existing; try to move current to another candidate
                        bool movedCurrent = false;
                        foreach (var alt in candidates)
                        {
                            if (alt == preferred) continue;
                            if (!assigned.ContainsKey(alt))
                            {
                                assigned[alt] = (pilot, idx, candidates);
                                movedCurrent = true;
                                break;
                            }
                        }
                        if (!movedCurrent)
                        {
                            // neither can be moved - skip current
                        }
                    }
                }
            }

            var toSet = new List<Tuple<Pilot, Channel>>();
            foreach (var kvp in assigned)
            {
                toSet.Add(new Tuple<Pilot, Channel>(kvp.Value.pilot, kvp.Key));
            }

            bool optimiseChannels = !SheetFormat.LockChannels;
            SheetFormatManager.EventManager.RaceManager.SetRacePilots(race, toSet, optimiseChannels);

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
