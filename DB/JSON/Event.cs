using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tools;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using RaceLib;

namespace DB.JSON
{
    public class Event : DatabaseObjectT<RaceLib.Event>
    {
        public string EventType { get; set; }
        public string Name { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public int Laps { get; set; }

        public int PBLaps { get; set; }

        public TimeSpan RaceLength { get; set; }

        public TimeSpan MinStartDelay { get; set; }

        public TimeSpan MaxStartDelay { get; set; }

        public string PrimaryTimingSystemLocation { get; set; }

        public TimeSpan RaceStartIgnoreDetections { get; set; }

        public TimeSpan MinLapTime { get; set; }

        public DateTime LastOpened { get; set; }

        public PilotChannel[] PilotChannels { get; set; }

        public Guid[] RemovedPilots { get; set; }

        public Guid[] Rounds { get; set; }

        public Guid Club { get; set; }

        public Guid[] Channels { get; set; }

        public bool Enabled { get; set; }

        public string MultiGPRaceFormat { get; set; }

        [Browsable(false)]
        public string SyncWith { get; set; }

        public bool Sync { get; set; }

        public Event()
        {
        }

        public Event(RaceLib.Event obj)
           : base(obj)
        {
            if (obj.Rounds != null)
                Rounds = obj.Rounds.Select(c => c.ID).ToArray();

            if (obj.PilotChannels != null)
                PilotChannels = obj.PilotChannels.Convert<PilotChannel>().ToArray();

            if (obj.Channels != null)
                Channels = obj.Channels.Select(c => c.ID).ToArray();

            if (obj.RemovedPilots != null)
                RemovedPilots = obj.RemovedPilots.Select(c => c.ID).ToArray();

            if (obj.Club != null)
                Club = obj.Club.ID;
        }

        public override RaceLib.Event GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Event ev = base.GetRaceLibObject(database);
            ev.Channels = Channels.Convert<RaceLib.Channel>(database).ToArray();
            ev.Club = Club.Convert<RaceLib.Club>(database);
            ev.PilotChannels = PilotChannels.Convert(database).Where(pc => pc != null && pc.Pilot != null).ToList();
            ev.Rounds = Rounds.Convert<RaceLib.Round>(database).ToList();
            ev.RemovedPilots = RemovedPilots.Convert<RaceLib.Pilot>(database).ToList();
           
            return ev;
        }

        public RaceLib.Event GetSimpleRaceLibEvent(ICollectionDatabase database)
        {
            RaceLib.Event ev = base.GetRaceLibObject(database);
            ev.Channels = Channels.Convert<RaceLib.Channel>(database).ToArray();
            ev.Club = Club.Convert<RaceLib.Club>(database);
            ev.Rounds = Rounds.Select(id => new RaceLib.Round() { ID = id }).ToList();

            ev.PilotChannels = PilotChannels.Where(pc => pc != null && pc.Pilot != Guid.Empty).Select(pc => new RaceLib.PilotChannel() { Pilot = new RaceLib.Pilot() { ID = pc.Pilot }, Channel = new RaceLib.Channel() { ID = pc.Channel } }).ToList();
            ev.RemovedPilots = RemovedPilots.Select(id => new RaceLib.Pilot() { ID = id }).ToList();

            return ev;
        }
    }
}
