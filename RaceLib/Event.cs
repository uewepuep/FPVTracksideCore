using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tools;

namespace RaceLib
{
    public enum EventTypes
    {
        Practice = 0,
        Practise = 0,

        TimeTrial = 1,
        
        Race = 2,
        
        Freestyle = 3,
        
        Endurance = 4,
        AggregateLaps = 4,
        Enduro = 4,
        
        CasualPractise = 5,
        CasualPractice = 5
    }

    public enum PrimaryTimingSystemLocation
    {
        EndOfLap = 0,
        Holeshot
    }

    public enum SyncWith
    {
        None,
        FPVTrackside,
        MultiGP,
        XClassRacing
    }

    public class Event : BaseDBObject
    {
        [System.ComponentModel.Browsable(false)]
        public EventTypes EventType { get; set; }


        [Category("Event Info")]
        public string Name { get; set; }

        [Category("Event Info")]
        [DisplayName("Start Date (y/m/d)")]
        [DateOnly]
        public DateTime Start { get; set; }

        [Category("Event Info")]
        [DisplayName("Club")]
        public string ClubName
        {
            get
            {
                if (Club == null)
                {
                    return "";
                }
                return Club.Name;
            }
        }

        [Category("Event Info")]
        [System.ComponentModel.Browsable(false)]
        [DateOnly]
        public DateTime End { get; set; }

        [Category("Event Info")]
        [DisplayName("Pilots Registered")]
        [LiteDB.BsonIgnore]
        public int PilotCount 
        { 
            get 
            {
                if (PilotChannels == null)
                    return 0;

                if (PilotChannels.Any())
                    return 0;

                return PilotChannels.Where(p => !p.Pilot.PracticePilot).Count();
            } 
        }
        
        [Category("Event Info")]
        [DisplayName("Channels")]
        [LiteDB.BsonIgnore]
        public string ChannelString 
        { 
            get
            {
                if (Channels == null) return "";

                return string.Join(", ", Channels.Select(c => c.GetBandChannelText()).ToArray()); 
            } 
        }

        [Category("Race Rules")]
        public int Laps { get; set; }

        [Category("Race Rules")]
        public int PBLaps { get; set; }

        [Category("Race Rules")]
        [DisplayName("Race Length (Seconds)")]
        public TimeSpan RaceLength { get; set; }

        [Category("Race Start")]
        [DisplayName("Minimum Start Delay (Seconds)")]
        public TimeSpan MinStartDelay { get; set; }

        [Category("Race Start")]
        [DisplayName("Maximum Start Delay (Seconds)")]
        public TimeSpan MaxStartDelay { get; set; }

        [Category("Track Layout")]
        public PrimaryTimingSystemLocation PrimaryTimingSystemLocation { get; set; }

        [Category("Track Layout")]
        [DisplayName("Race Start Ignore Detections (Seconds)")]
        public TimeSpan RaceStartIgnoreDetections { get; set; }

        [Category("Track Layout")]
        [DisplayName("Smart-Minimum Lap Time (Seconds)")]
        public TimeSpan MinLapTime { get; set; }

        [System.ComponentModel.Browsable(false)]
        public DateTime LastOpened { get; set; }

        [System.ComponentModel.Browsable(false)]
        [LiteDB.BsonIgnore]
        public IEnumerable<Pilot> Pilots { get { return PilotChannels.Select(pc => pc.Pilot); } }

        [System.ComponentModel.Browsable(false)]
        [LiteDB.BsonRef("PilotChannel")]
        public List<PilotChannel> PilotChannels { get; set; }
        [System.ComponentModel.Browsable(false)]
        [LiteDB.BsonRef("Pilot")]
        public List<Pilot> RemovedPilots { get; set; }

        [System.ComponentModel.Browsable(false)]
        [LiteDB.BsonRef("Track")]
        public List<Track> Tracks { get; set; }

        [System.ComponentModel.Browsable(false)]
        [LiteDB.BsonRef("Round")]
        public List<Round> Rounds { get; set; }

        [System.ComponentModel.Browsable(false)]
        [LiteDB.BsonRef("Club")]
        public Club Club { get; set; }

        [System.ComponentModel.Browsable(false)]
        [LiteDB.BsonRef("Channel")]
        public Channel[] Channels { get; set; }

        [System.ComponentModel.Browsable(false)]
        public bool Enabled { get; set; }

        [System.ComponentModel.Browsable(false)]
        public MultiGPRaceFormat MultiGPRaceFormat { get; set; }
        
        [System.ComponentModel.Browsable(false)]
        public SyncWith SyncWith { get; set; }

        [Category("Cloud")]
        public bool Sync { get; set; }

        [LiteDB.BsonIgnore]
        [Category("Cloud")]
        [DisplayName("Sync Service")]
        public string SyncService
        {
            get
            {
                if (!Sync || SyncWith == SyncWith.None)
                    return "None";

                switch (SyncWith)
                {
                    case SyncWith.XClassRacing:
                        return "xclass.racing";
                    default:
                        return SyncWith.ToString().ToLower() + ".com";
                }
            }
        }
        
        [System.ComponentModel.Browsable(false)]
        [LiteDB.BsonIgnore]
        public string Month
        {
            get
            {
                return System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Start.Month) + " " + Start.Year;
            }
        }

        //[Category("MultiGP")]

        //[DisplayName("ZippyQ")]
        //public bool MultiGPZippyQ { get; set; }

        public Event()
        {
            Sync = false;
            SyncWith = SyncWith.FPVTrackside;
            Enabled = true;
            PrimaryTimingSystemLocation = PrimaryTimingSystemLocation.Holeshot;
            RaceStartIgnoreDetections = TimeSpan.FromSeconds(2);
            PBLaps = 1;
            PilotChannels = new List<PilotChannel>();
            RemovedPilots = new List<Pilot>();
            RaceLength = TimeSpan.FromMinutes(2);
            Laps = 4;
            EventType = EventTypes.Race;
            Name = "New Event";
            MinStartDelay = TimeSpan.FromSeconds(0.5f);
            MaxStartDelay = TimeSpan.FromSeconds(5);
            Tracks = new List<Track>();

            Rounds = new List<Round>();
            Channels = new Channel[0];
            MinLapTime = TimeSpan.FromSeconds(5);
            Start = DateTime.Today;
        }

        public Event Clone()
        {
            Event newEvent = new Event();
            newEvent.Club = this.Club;
            newEvent.MinStartDelay = this.MinStartDelay;
            newEvent.MaxStartDelay = this.MaxStartDelay;

            newEvent.RaceStartIgnoreDetections = this.RaceStartIgnoreDetections;
            newEvent.PrimaryTimingSystemLocation = this.PrimaryTimingSystemLocation;

            newEvent.Laps = this.Laps;
            newEvent.PBLaps = this.PBLaps;

            newEvent.RaceLength = this.RaceLength;
            newEvent.EventType = this.EventType;

            newEvent.Name = this.Name + " Clone";

            newEvent.PilotChannels = this.PilotChannels.ToList();
            newEvent.Channels = this.Channels.ToArray();
            newEvent.Tracks = this.Tracks.ToList();

            newEvent.MinLapTime = this.MinLapTime;

            return newEvent;
        }

        public override string ToString()
        {
            switch (SyncWith)
            {
                case SyncWith.None:
                case SyncWith.FPVTrackside:
                    return Name;

                default:
                    return Name + " (" + SyncWith.ToString() +")";
            }
        }

        public static IEnumerable<EventTypes> GetEventTypes()
        {
            yield return EventTypes.Practice;
            yield return EventTypes.TimeTrial;
            yield return EventTypes.Race;
            yield return EventTypes.Endurance;
            yield return EventTypes.Freestyle;
            yield return EventTypes.CasualPractice;
        }
        public void RefreshPilots(Database db)
        {
            foreach (PilotChannel pc in PilotChannels)
            {
                Pilot p = db.Pilots.GetObject(pc.Pilot.ID);
                pc.Pilot = p;
            }
        }
    }
}
