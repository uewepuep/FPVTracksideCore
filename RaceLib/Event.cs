using Microsoft.Xna.Framework;
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

    public class Event : BaseObject
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
        
        [Category("Event Info")]
        [DisplayName("Channels")]
        
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
        [DisplayName("Smart Minimum Lap Time (Seconds)")]
        public TimeSpan MinLapTime { get; set; }

        [System.ComponentModel.Browsable(false)]
        public DateTime LastOpened { get; set; }

        [System.ComponentModel.Browsable(false)]
        
        public IEnumerable<Pilot> Pilots { get { return PilotChannels.Select(pc => pc.Pilot).Where(p => p != null); } }

        [System.ComponentModel.Browsable(false)]
        public List<PilotChannel> PilotChannels { get; set; }
        [System.ComponentModel.Browsable(false)]
        public List<Pilot> RemovedPilots { get; set; }

        [System.ComponentModel.Browsable(false)]
        public List<Round> Rounds { get; set; }

        [System.ComponentModel.Browsable(false)]
        public Club Club { get; set; }

        [System.ComponentModel.Browsable(false)]
        public Channel[] Channels { get; set; }

        [System.ComponentModel.Browsable(false)]
        public bool Enabled { get; set; }

        [System.ComponentModel.Browsable(false)]
        public MultiGPRaceFormat MultiGPRaceFormat { get; set; }
        
        [Category("Cloud")]
        [DisplayName("Sync with FPVTrackside.com")]
        public bool SyncWithFPVTrackside { get; set; }

        [Category("Cloud")]
        [DisplayName("Visible on FPVTrackside.com")]
        [System.ComponentModel.Browsable(false)]
        public bool VisibleOnline { get; set; }

        [Category("Cloud")]
        [DisplayName("Sync with MultiGP")]
        public bool SyncWithMultiGP { get; set; }

        [Category("Cloud")]
        [DisplayName("Generate heats on MultiGP.com (ZippyQ etc)")]
        public bool GenerateHeatsMultiGP { get; set; }


        [System.ComponentModel.Browsable(false)]
        public string[] ChannelColors { get; set; }

        [System.ComponentModel.Browsable(false)]
        public string Month
        {
            get
            {
                return System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Start.Month) + " " + Start.Year;
            }
        }

        [System.ComponentModel.Browsable(false)]
        public bool Locked { get; set; }

        [Browsable(false)]
        public Track Track { get; set; }

        [Browsable(false)]
        public Sector[] Sectors { get; set; }

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
            Name = "New Event";
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

            newEvent.Name = this.Name + " Clone";

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
}
