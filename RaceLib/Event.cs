using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tools;

namespace RaceLib
{
    public enum EventTypes
    {
        Unknown = -1,
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

    public class Event : BaseObject
    {
        [System.ComponentModel.Browsable(false)]
        public EventTypes EventType { get; set; }


        public string Name { get; set; }

        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public int PilotCount 
        { 
            get 
            {
                if (PilotChannels == null)
                    return 0;

                if (!PilotChannels.Any())
                    return 0;

                return PilotChannels.Where(p => p.Pilot != null && !p.Pilot.PracticePilot).Count();
            } 
        }

        public int Laps { get; set; }

        public int PBLaps { get; set; }

        public TimeSpan RaceLength { get; set; }

        public TimeSpan MinStartDelay { get; set; }

        public TimeSpan MaxStartDelay { get; set; }

        public PrimaryTimingSystemLocation PrimaryTimingSystemLocation { get; set; }

        public TimeSpan RaceStartIgnoreDetections { get; set; }
        public TimeSpan MinLapTime { get; set; }

        public DateTime LastOpened { get; set; }

        public IEnumerable<Pilot> Pilots { get { return PilotChannels.Select(pc => pc.Pilot).Where(p => p != null); } }

        public List<PilotChannel> PilotChannels { get; set; }
        public List<Pilot> RemovedPilots { get; set; }

        public List<Round> Rounds { get; set; }

        public Club Club { get; set; }

        public Channel[] Channels { get; set; }

        public bool Enabled { get; set; }
        
        public bool SyncWithFPVTrackside { get; set; }

        public bool VisibleOnline { get; set; }

        public MultiGPRaceFormat MultiGPRaceFormat { get; set; }

        public bool SyncWithMultiGP { get; set; }

        public bool GenerateHeatsMultiGP { get; set; }

        public string[] ChannelColors { get; set; }
        
        public bool Locked { get; set; }

        public Track Track { get; set; }

        public Sector[] Sectors { get; set; }

        private static string dateFormat = "d MMM";

        public DateTime[] Flags { get; set; }

        public Event()
        {
            SyncWithFPVTrackside = false;
            SyncWithMultiGP = false;
            GenerateHeatsMultiGP = false;
            VisibleOnline = true;
            Enabled = true;
            PrimaryTimingSystemLocation = PrimaryTimingSystemLocation.Holeshot;
            RaceStartIgnoreDetections = TimeSpan.FromSeconds(0.5);
            PBLaps = 1;
            PilotChannels = new List<PilotChannel>();
            RemovedPilots = new List<Pilot>();
            RaceLength = TimeSpan.FromMinutes(2);
            Laps = 4;
            EventType = EventTypes.Race;
            Start = DateTime.Today;
            Name = "New Event (" + Start.ToString(dateFormat) + ")";
            MinStartDelay = TimeSpan.FromSeconds(0.5f);
            MaxStartDelay = TimeSpan.FromSeconds(5);

            Rounds = new List<Round>();
            Channels = new Channel[0];
            MinLapTime = TimeSpan.FromSeconds(5);
            Start = DateTime.Today;
            VisibleOnline = true;
            Sectors = new Sector[0];
        }

        public Event Clone()
        {
            Event newEvent = new Event();
            newEvent.ID = Guid.NewGuid();
            newEvent.Club = this.Club;
            newEvent.MinStartDelay = this.MinStartDelay;
            newEvent.MaxStartDelay = this.MaxStartDelay;

            newEvent.RaceStartIgnoreDetections = this.RaceStartIgnoreDetections;
            newEvent.PrimaryTimingSystemLocation = this.PrimaryTimingSystemLocation;

            newEvent.Laps = this.Laps;
            newEvent.PBLaps = this.PBLaps;

            newEvent.RaceLength = this.RaceLength;
            newEvent.EventType = this.EventType;

            newEvent.Name = this.Name;
            newEvent.Start = DateTime.Today;

            try
            {
                newEvent.Name = Regex.Replace(newEvent.Name, @"\([A-z0-9 ]*\)", "");
            }
            catch
            {
            }

            newEvent.Name = newEvent.Name + " (" +  Start.ToString(dateFormat) + ")";

            newEvent.PilotChannels = this.PilotChannels.ToList();
            newEvent.Channels = this.Channels.ToArray();

            newEvent.MinLapTime = this.MinLapTime;

            return newEvent;
        }

        public override string ToString()
        {
            return Name;
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

        public void RefreshPilots(IEnumerable<Pilot> editedPilots)
        {
            foreach (PilotChannel pc in PilotChannels)
            {
                if (pc != null && pc.Pilot != null)
                {
                    Pilot p = editedPilots.GetObject(pc.Pilot.ID);
                    if (p != null)
                    {
                        pc.Pilot = p;
                    }
                }
            }
        }
    }

    public class SimpleEvent
    {
        [Browsable(false)]
        public Guid ID { get; set; }


        [Category("Event Info")]
        public string Name { get; set; }

        [Category("Event Info")]
        [DisplayName("Start Date (y/m/d)")]
        [DateOnly]
        public DateTime Start { get; set; }

        
        [Category("Event Info")]
        [DisplayName("Pilots Registered")]

        public int PilotsRegistered { get; set; }

        [Category("Event Info")]
        [DisplayName("Channels")]
        public string ChannelsString { get; set; }

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
        [DisplayName("Smart Minimum Lap Time (Seconds)")]
        public TimeSpan MinLapTime { get; set; }

        [Category("Cloud")]
        [DisplayName("Sync with FPVTrackside.com")]
        public bool SyncWithFPVTrackside { get; set; }

        [Category("Cloud")]
        [DisplayName("Visible on FPVTrackside.com")]
        public bool VisibleOnline { get; set; }

        [System.ComponentModel.Browsable(false)]
        public MultiGPRaceFormat MultiGPRaceFormat { get; set; }

        [Category("Cloud")]
        [DisplayName("Sync with MultiGP")]
        public bool SyncWithMultiGP { get; set; }

        [Category("Cloud")]
        [DisplayName("Generate heats on MultiGP.com (ZippyQ etc)")]
        public bool GenerateHeatsMultiGP { get; set; }
        [System.ComponentModel.Browsable(false)]
        [Category("Cloud")]
        public int ExternalID { get; set; }


        [System.ComponentModel.Browsable(false)]
        public string Month
        {
            get
            {
                return System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Start.Month) + " " + Start.Year;
            }
        }

        [Browsable(false)]
        public DateTime LastOpened { get; set; }
        
        [Browsable(false)]
        public bool Enabled { get; set; }

        [Browsable(false)]
        public bool Locked { get; set; }

        public SimpleEvent(Guid id)
        {
            ID = id;
        }

        public SimpleEvent(Event eventt)
            :this(eventt.ID)
        {
            ReflectionTools.Copy(eventt, this);
            PilotsRegistered = eventt.PilotCount;
            ChannelsString = string.Join(", ", eventt.Channels.Select(c => c.GetBandChannelText()).ToArray());
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
