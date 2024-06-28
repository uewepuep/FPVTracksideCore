using Microsoft.Xna.Framework;
using RaceLib.Format;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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



        public EventManager(Profile profile)
        {
            Profile = profile;
            RaceManager = new RaceManager(this);
            LapRecordManager = new LapRecordManager(RaceManager);
            ResultManager = new ResultManager(this);
            TimedActionManager = new TimedActionManager();
            RoundManager = new RoundManager(this);
            SpeedRecordManager = new SpeedRecordManager(RaceManager);

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

            FindProfilePicture(pc.Pilot);

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
            if (Event.Locked)
                return;

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                Event.Laps = laps;
                db.Update(Event);
            }

            RaceManager.SetTargetLaps(laps);

            OnEventChange?.Invoke();
        }

        public void SetEventType(EventTypes type)
        {
            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                Event.EventType = type;
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

        public void LoadEvent(WorkSet workSet, WorkQueue workQueue, Event eve)
        {
            EventId = eve.ID;

            workQueue.Enqueue(workSet, "Loading Event", () =>
            {
                using (IDatabase db = DatabaseFactory.OpenLegacyLoad(EventId))
                {
                    Event = db.LoadEvent();
                    System.Diagnostics.Debug.Assert(Event != null);

                    UpdateRoundOrder(db);
                }

                if (Event.Channels == null || !Event.Channels.Any())
                {
                    Event.Channels = Channel.Read(Profile);
                }
            });

            workQueue.Enqueue(workSet, "Finding Profile Pictures", () =>
            {
                FindProfilePictures(Event.Pilots.ToArray());
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

        public IEnumerable<FileInfo> GetPilotProfileMedia()
        {
            DirectoryInfo pilotProfileDirectory = new DirectoryInfo("pilots");
            string[] extensions = new string[] { ".mp4", ".wmv", ".mkv", ".png", ".jpg"};

            if (pilotProfileDirectory.Exists)
            {
                foreach (FileInfo file in pilotProfileDirectory.GetFiles())
                {
                    if (extensions.Contains(file.Extension))
                    {
                        yield return file;
                    }
                }
            }
        }

        public void FindProfilePicture(Pilot pilot)
        {
            FindProfilePictures(new[] { pilot });
        }

        public void FindProfilePictures(Pilot[] pilots)
        {
            string currentDirectory = Directory.GetCurrentDirectory();

            FileInfo[] media = GetPilotProfileMedia().ToArray();
            foreach (Pilot p in pilots)
            {
                if (p != null)
                {
                    string oldPath = p.PhotoPath;

                    if (string.IsNullOrEmpty(p.PhotoPath))
                    {
                        IEnumerable<FileInfo> matches = media.Where(f => f.Name.ToLower().Contains(p.Name.ToLower()));
                        if (matches.Any())
                        {
                            p.PhotoPath = matches.OrderByDescending(f => f.Extension).FirstOrDefault().FullName;
                        }
                    }
                    if (!string.IsNullOrEmpty(p.PhotoPath))
                    {
                        p.PhotoPath = Path.GetRelativePath(currentDirectory, p.PhotoPath);
                    }
                }
            }

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(pilots);
            }
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

        public void LoadRaces(WorkSet workSet, WorkQueue workQueue, Event eve)
        {
            workQueue.Enqueue(workSet, "Loading Races", () =>
            {
                //Load any existing races
                RaceManager.LoadRaces(eve);
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
        
        public Microsoft.Xna.Framework.Color GetPilotColor(Pilot p)
        {
            Channel channel = GetChannel(p);
            return GetChannelColor(channel);
        }

        public Microsoft.Xna.Framework.Color GetChannelColor(Channel c)
        {
            Microsoft.Xna.Framework.Color color;
            if (channelColour.TryGetValue(c, out color))
            {
                return color;
            }
            return  Microsoft.Xna.Framework.Color.Red;
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

        public string GetResultsText(Units units)
        {
            Race currentRace = RaceManager.CurrentRace;
            if (currentRace != null)
            {
                string textResults = ResultManager.GetResultsText(currentRace, units);

                return textResults;
            }

            return "";
        }

        public Event[] GetOtherEvents()
        {
            using (IDatabase db = DatabaseFactory.OpenLegacyLoad(Guid.Empty))
            {
                return db.GetEvents().Where(e => e.ID != Event.ID).ToArray();
            }
        }

        public Pilot[] GetOtherEventPilots(Event e)
        {
            using (IDatabase db = DatabaseFactory.OpenLegacyLoad(e.ID))
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

        public IEnumerable<Channel> GetChannelGroup(int slot)
        {
            int i = 0;
            foreach (var channelGroup in Channels.GetChannelGroups())
            {
                if (i == slot)
                {
                    return channelGroup;
                }
                i++;
            }

            return new Channel[0];
        }
    }
}
