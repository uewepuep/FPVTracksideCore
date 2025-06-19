using Microsoft.Xna.Framework;
using RaceLib.Format;
using RaceLib.Game;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Tools;

namespace RaceLib
{
    public class EventManager : IDisposable
    {
        public RaceManager RaceManager { get; private set; }

        public Channel[] Channels { get { return Event.Channels; } }

        private Dictionary<Channel, Microsoft.Xna.Framework.Color> channelColour;

        private Event eventObj;
        public Event Event { get { return eventObj; } set { eventObj = value; OnEventChange?.Invoke(); } }
        
        public Guid EventId { get; private set; }


        public event System.Action OnEventChange;
        public event System.Action OnPilotRefresh;

        public LapRecordManager LapRecordManager { get; set; }
        public ResultManager ResultManager { get; set; }
        public TimedActionManager TimedActionManager { get; set; }
        public RoundManager RoundManager { get; set; }
        public SpeedRecordManager SpeedRecordManager { get; set; }

        public event PilotChannelDelegate OnPilotChangedChannels;

        public event System.Action OnChannelsChanged;

        public ExportColumn[] ExportColumns { get; set; }

        public Profile Profile { get; private set; }

        public TrackFlightPath FlightPath { get; set; }

        public GameManager GameManager { get; set; }
        public RaceStringFormatter RaceStringFormatter { get; private set; }
        public ExternalRaceProvider[] ExternalRaceProviders { get; set; }

        public ProfilePictures ProfilePictures { get; private set; }
        public EventManager(Profile profile)
        {
            Profile = profile;
            RaceManager = new RaceManager(this);
            LapRecordManager = new LapRecordManager(RaceManager);
            ResultManager = new ResultManager(this);
            TimedActionManager = new TimedActionManager();
            RoundManager = new RoundManager(this);
            SpeedRecordManager = new SpeedRecordManager(RaceManager);
            GameManager = new GameManager(this);

            RaceStringFormatter = new RaceStringFormatter(this);

            ExportColumns = ExportColumn.Read(Profile);

            channelColour = new Dictionary<Channel, Microsoft.Xna.Framework.Color>();

            RaceManager.TimingSystemManager.Connect();

        }

        public void Dispose()
        {
            RaceManager.Dispose();
        }

        public Channel GetChannel(Pilot p)
        {
            PilotChannel pc = GetPilotChannel(p);
            if (pc != null)
            {
                return pc.Channel;
            }

            return Channels.FirstOrDefault();
        }

        public BandType GetBandType(Pilot p)
        {
            Channel channel = GetChannel(p);
            if (channel != null)
                return channel.Band.GetBandType();

            return BandType.Analogue;
        }

        public Pilot GetCreatePilot(string pilotName)
        {
            Pilot p = null;

            // Check the event..
            var pc = eventObj.PilotChannels.FirstOrDefault(pa => pa != null && pa.Pilot != null && !string.IsNullOrEmpty(pa.Pilot.Name) && pa.Pilot.Name.ToLower() == pilotName.ToLower());

            if (pc != null)
            {
                return pc.Pilot;
            }

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                // check the db.
                if (pc == null)
                {
                    p = db.All<Pilot>().FirstOrDefault(pa => !string.IsNullOrEmpty(pa.Name) && pa.Name.ToLower() == pilotName.ToLower());
                }

                // Create
                if (p == null)
                {
                    p = Pilot.CreateFromName(pilotName);
                    db.Insert(p);
                }
            }
            return p;
        }

        public bool AddPilot(Pilot p)
        {
            Channel channel = LeastUsedChannel();

            return AddPilot(p, channel);
        }

        public Channel LeastUsedChannel()
        {
            Dictionary<Channel, int> counts = new Dictionary<Channel, int>();
            foreach (Channel[] shared in Channels.GetChannelGroups())
            {
                int count = Event.PilotChannels.Count(r => r.Channel.InterferesWith(shared));
                counts.Add(shared.FirstOrDefault(), count);
            }

            if (counts.Any())
            {
                return counts.OrderBy(kvp => kvp.Value).First().Key;
            }

            return Channels.FirstOrDefault();
        }

        public bool AddPilot(Pilot p, Channel c)
        {
            return AddPilot(new PilotChannel(p, c));
        }

        public bool AddPilot(PilotChannel pc)
        {
            if (eventObj.PilotChannels.Any(a => a.Pilot == pc.Pilot))
            {
                return false;
            }

            eventObj.PilotChannels.Add(pc);

            if (eventObj.RemovedPilots.Contains(pc.Pilot))
            {
                eventObj.RemovedPilots.Remove(pc.Pilot);
            }

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Upsert(pc);
                db.Upsert(pc.Pilot);
                db.Update(eventObj);
            }

            OnPilotRefresh?.Invoke();

            ProfilePictures.FindProfilePicture(pc.Pilot);

            return true;
        }

        public bool RemovePilot(Pilot pilot)
        {
            PilotChannel pilotChannel = GetPilotChannel(pilot); 
            if (pilotChannel != null)
            {
                Event.PilotChannels.Remove(pilotChannel);
                Event.RemovedPilots.Add(pilotChannel.Pilot);

                using (IDatabase db = DatabaseFactory.Open(EventId))
                {
                    db.Update(eventObj);
                }
                OnPilotRefresh?.Invoke();
                return true;
            }

            return false;
        }

        public void SetEventLaps(int laps)
        {
            if (Event.RulesLocked)
                return;

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                Event.Laps = laps;
                db.Update(Event);
            }

            RaceManager.SetTargetLaps(laps);

            OnEventChange?.Invoke();
        }

        public void SetEventType(EventTypes type, GameType gameType = null)
        {
            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                Event.EventType = type;

                if (gameType != null)
                {
                    Event.GameTypeName = gameType.Name;
                }
                else
                {
                    Event.GameTypeName = null;
                }

                db.Update(Event);

                Race current = RaceManager.CurrentRace;
                if (current != null)
                {
                    current.Round.EventType = type;
                    db.Update(current.Round);
                }
            }

            OnEventChange?.Invoke();
        }

        public void SetRaceLength(int seconds)
        {
            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                Event.RaceLength = TimeSpan.FromSeconds(seconds);
                db.Update(Event);
            }

            OnEventChange?.Invoke();
        }

        public void LoadEvent(WorkSet workSet, WorkQueue workQueue, Guid eventId)
        {
            EventId = eventId;
            ProfilePictures = new ProfilePictures(EventId);

            workQueue.Enqueue(workSet, "Loading Event", () =>
            {
                using (IDatabase db = DatabaseFactory.Open(EventId))
                {
                    Event = db.LoadEvent();

                    Event.LastOpened = DateTime.Now;
                    db.Update(Event);

                    System.Diagnostics.Debug.Assert(Event != null);

                    UpdateRoundOrder(db);
                }

                if (Event.Channels == null || !Event.Channels.Any())
                {
                    Event.Channels = Channel.LoadDisplayNames(Profile);
                }
                else
                {
                    Channel.LoadDisplayNames(Event);
                }

            });

            workQueue.Enqueue(workSet, "Loading Game Types", () =>
            {
                GameManager.LoadGameTypes(Profile);
                
                GameType gameType = GameManager.GetByName(Event.GameTypeName);
                GameManager.SetGameType(gameType);
            });

            workQueue.Enqueue(workSet, "Finding Profile Pictures", () =>
            {
                ProfilePictures.FindProfilePictures(Event.Pilots.ToArray());
            });

            workQueue.Enqueue(workSet, "Updating event object", () =>
            {
                using (IDatabase db = DatabaseFactory.Open(EventId))
                {
                    db.Update(Event);
                }
            });

            workQueue.Enqueue(workSet, "Loading Track", () =>
            {
                LoadTrack(Event.Track);
            });
        }

        public void LoadTrack(Track track)
        {
            Event.Track = track;
            FlightPath = new TrackFlightPath(Event.Track);
        }


        public void UpdateRoundOrder(IDatabase db)
        {
            if (Event.Rounds != null && Event.Rounds.Any(r => r.Order < 0))
            {
                Round[] rounds = Event.Rounds.OrderBy(r => r.Order).ThenBy(r => r.Creation).ThenBy(r => r.RoundNumber).ToArray();

                int order = 100;
                foreach (Round r in rounds)
                {
                    r.Order = order;
                    order += 100;
                }

                db.Update(rounds);
            }
        }

        public void LoadRaces(WorkSet workSet, WorkQueue workQueue)
        {
            workQueue.Enqueue(workSet, "Loading Races", () =>
            {
                //Load any existing races
                RaceManager.LoadRaces(Event);
            });

            workQueue.Enqueue(workSet, "RoundRepair", RoundRepair);

            workQueue.Enqueue(workSet, "Loading Results", () =>
            {
                // Load points
                ResultManager.Load(Event);
            });

            workQueue.Enqueue(workSet, "Updating Records", () =>
            {
                LapRecordManager.UpdatePilots(Event.PilotChannels.Select(pc => pc.Pilot));
                SpeedRecordManager.Initialize();
            });

            workQueue.Enqueue(workSet, "Loading Sheets", () =>
            {
                RoundManager.SheetFormatManager.Load();
            });
        }

        private void RoundRepair()
        {
            IEnumerable<Round> allRounds = Event.Rounds.Union(RaceManager.Races.Select(r => r.Round)).Distinct();

            if (allRounds.Count() > Event.Rounds.Count)
            {
                Event.Rounds = allRounds.OrderBy(r => r.Order).ToList();
            }
        }

        public void Update(GameTime gameTime)
        {
            if (RaceManager != null)
            {
                RaceManager.Update(gameTime);
            }

            if (TimedActionManager != null)
            {
                TimedActionManager.Update();
            }

            if (GameManager != null)
            {
                GameManager.Update(gameTime);
            }
        }

        public void SetChannelColors(IEnumerable<Microsoft.Xna.Framework.Color> colors)
        {
            channelColour.Clear();

            if (Channels == null || !Channels.Any())
                return;

            var ordered = Channels.OrderBy(c => c.Frequency).ThenBy(r => r.Band);
            var colorEnumer = colors.GetEnumerator();

            Channel last = null;
            foreach (Channel channel in ordered)
            {
                if (!channel.InterferesWith(last))
                {
                    if (!colorEnumer.MoveNext())
                    {
                        colorEnumer = colors.GetEnumerator();
                        colorEnumer.MoveNext();
                    }
                }

                Color color = colorEnumer.Current;
                if (!channelColour.ContainsKey(channel))
                {
                    channelColour.Add(channel, color);
                }
                last = channel;
            }

            Event.ChannelColors = colors.Select(c => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2")).ToArray();

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(Event);
            }

            OnChannelsChanged?.Invoke();
        }
        
        public Color GetPilotColor(Pilot p)
        {
            Channel channel = GetChannel(p);
            return GetChannelColor(channel);
        }

        public Color GetCurrentRaceChannelColor(Channel c)
        {
            Race race = RaceManager.CurrentRace;
            return GetRaceChannelColor(race, c);
        }

        public Color GetRaceChannelColor(Race race, Channel c)
        {
            if (race != null && race.Round != null && race.Round.EventType == EventTypes.Game)
            {
                return GameManager.GetTeamColor(c);
            }
            return GetChannelColor(c);
        }

        public Color GetChannelColor(Channel c)
        {
            Color color;
            if (channelColour.TryGetValue(c, out color))
            {
                return color;
            }
            return Color.Red;
        }

        public void RemovePilots()
        {
            eventObj.RemovedPilots.AddRange(eventObj.PilotChannels.Where(p => !eventObj.RemovedPilots.Contains(p.Pilot)).Select(pc => pc.Pilot));
            eventObj.PilotChannels.Clear();

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(eventObj);
            }
        }

        public void RedistrubuteChannels()
        {
            var channelLanes = Channels.GetChannelGroups().ToArray();

            int counter = 0;
            foreach (var p in Event.PilotChannels.OrderBy(p => p.Pilot.Name))
            {
                Channel c = channelLanes[counter % channelLanes.Length].First();

                SetPilotChannel(p.Pilot, c);
                counter++;
            }
        }

        public PilotChannel GetPilotChannel(Pilot p)
        {
            return Event.PilotChannels.FirstOrDefault(pc => pc.Pilot == p);
        }

        public void SetPilotChannels(Race race)
        {
            var cache = race.PilotChannelsSafe;
            foreach (var pc in cache)
            {
                SetPilotChannel(pc.Pilot, pc.Channel);
            }
        }

        public void SetPilotChannel(Pilot pi, Channel c)
        {
            if (pi == null)
                return;

            PilotChannel pc = GetPilotChannel(pi);
            if (pc == null)
                return;

            if (pc.Channel != c)
            {
                pc.Channel = c;

                using (IDatabase db = DatabaseFactory.Open(EventId))
                {
                    db.Update(pc);
                }

                if (RaceManager.HasPilot(pi) && !RaceManager.RaceRunning)
                {
                    RaceManager.ChangeChannel(c, pi);
                }

                OnPilotChangedChannels?.Invoke(pc);
            }
        }

        public void ToggleSumPoints(Round round)
        {
            if (round.PointSummary == null)
            {
                round.PointSummary = new PointSummary(ResultManager.PointsSettings);
            }
            else
            {
                round.PointSummary = null;
            }

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(Event);
                db.Update(round);
            }
        }

        public void ToggleTimePoints(Round round, TimeSummary.TimeSummaryTypes type)
        {
            if (round.TimeSummary == null)
            {
                round.TimeSummary = new TimeSummary() { TimeSummaryType = type };
            }
            else
            {
                round.TimeSummary = null;
            }

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(Event);
                db.Update(round);
            }
        }

        public void ToggleLapCount(Round round)
        {
            round.LapCountAfterRound = !round.LapCountAfterRound;

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(Event);
                db.Update(round);
            }
        }

        public void TogglePackCount(Round round)
        {
            round.PackCountAfterRound = !round.PackCountAfterRound;

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(Event);
                db.Update(round);
            }
        }

        public bool CanExport(ExportColumn.ColumnTypes type)
        {
            return ExportColumns.Any(ec => ec.Enabled && ec.Type == type);
        }


        public IEnumerable<Tuple<Pilot, Channel>> GetPilotsFromLines(IEnumerable<string> pilots, bool assignChannel)
        {
            int channelIndex = 0;

            var channelLanes = Channels.GetChannelGroups().ToArray();

            foreach (string untrimmed in pilots)
            {
                string pilotname = untrimmed;

                string[] csv = pilotname.Split(',');    
                if (csv.Length > 0)
                {
                    pilotname = csv[0];
                }

                pilotname = pilotname.Trim();

                Pilot p = Event.Pilots.FirstOrDefault(pa => pa != null && pa.Name.ToLower() == pilotname.ToLower());
                if (p != null)
                {
                    Channel c = GetChannel(p);
                    if (assignChannel)
                    {
                        if (channelIndex < channelLanes.Length)
                        {
                            var laneOptions = channelLanes[channelIndex];

                            var chosen = laneOptions.FirstOrDefault(r => r.Band.GetBandType() == c.Band.GetBandType());
                            if (chosen != null)
                            {
                                c = chosen;
                            }
                        }
                    }

                    yield return new Tuple<Pilot, Channel>(p, c);
                }
                channelIndex++;
                channelIndex = channelIndex % Channels.Length;
            }
        }

        public void AddPilotsFromLines(IEnumerable<string> pilots)
        {
            IEnumerable<Tuple<Pilot, Channel>> pcs = GetPilotsFromLines(pilots, true);
            foreach (Tuple<Pilot, Channel> pc in pcs)
            {
                Pilot p = pc.Item1;
                Channel c = pc.Item2;

                RaceManager.AddPilot(c, p);
            }
        }

        public string[][] GetResultsText(Units units)
        {
            Race currentRace = RaceManager.CurrentRace;
            if (currentRace != null)
            {
                return ResultManager.GetResultsText(currentRace, units);
            }

            return new string[0][];
        }

        public SimpleEvent[] GetOtherEvents()
        {
            using (IDatabase db = DatabaseFactory.Open(Guid.Empty))
            {
                return db.GetSimpleEvents().Where(e => e.ID != Event.ID).ToArray();
            }
        }

        public Pilot[] GetOtherEventPilots(SimpleEvent e)
        {
            using (IDatabase db = DatabaseFactory.Open(e.ID))
            {
                Event loaded = db.LoadEvent();
                return loaded.Pilots.ToArray();
            }
        }

        public Pilot GetPilot(Guid iD)
        {
            return Event.Pilots.FirstOrDefault(p => p.ID == iD);
        }

        public Pilot GetPilot(string name)
        {
            return Event.Pilots.FirstOrDefault(p => p.Name == name);
        }

        public void RefreshPilots(IEnumerable<Pilot> editedPilots)
        {

            Event.RefreshPilots(editedPilots);
            foreach (Race r in RaceManager.Races)
            {
                r.RefreshPilots(editedPilots);
            }

            OnPilotRefresh?.Invoke();
        }

        public int GetMaxPilotsPerRace()
        {
            return Channels.GetChannelGroups().Count();
        }

        public int GetChannelGroupIndex(Channel channel)
        {
            int i = 0;
            foreach (var channelGroup in Channels.GetChannelGroups())
            {
                if (channelGroup.Contains(channel))
                {
                    return i;
                }
                i++;
            }
            return -1;
        }

        public IEnumerable<Channel> GetChannelGroup(int index)
        {
            int i = 0;
            foreach (var channelGroup in Channels.GetChannelGroups())
            {
                if (i == index)
                {
                    return channelGroup;
                }
                i++;
            }

            return new Channel[0];
        }

        public void AddFlag()
        {
            List<DateTime> flags = new List<DateTime>();
            if (Event.Flags != null)
            {
                flags.AddRange(Event.Flags);
            }

            flags.Add(DateTime.Now);

            Event.Flags = flags.ToArray();

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(Event);
            }
        }
    }
}
