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

namespace DB.Lite
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

        [DBRef("PilotChannel")]
        public List<PilotChannel> PilotChannels { get; set; }

        [DBRef("Pilot")]
        public List<Pilot> RemovedPilots { get; set; }

        [DBRef("Round")]
        public List<Round> Rounds { get; set; }

        [DBRef("Club")]
        public Club Club { get; set; }

        [DBRef("Channel")]
        public Channel[] Channels { get; set; }

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
                Rounds = obj.Rounds.Convert<Round>().ToList();

            if (obj.PilotChannels != null)
                PilotChannels = obj.PilotChannels.Convert<PilotChannel>().ToList();

            if (obj.Channels != null)
                Channels = obj.Channels.Convert<Channel>().ToArray();

            if (obj.RemovedPilots != null)
                RemovedPilots = obj.RemovedPilots.Convert<Pilot>().ToList();

            if (obj.Club != null)
                Club = obj.Club.Convert<Club>();
        }

        public override RaceLib.Event GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Event ev = base.GetRaceLibObject(database);

            ev.Channels = Channels.Convert(database).ToArray();
            ev.Club = Club.Convert(database);
            ev.PilotChannels = PilotChannels.Convert(database).ToList();
            ev.Rounds = Rounds.Convert(database).ToList();
            ev.RemovedPilots = RemovedPilots.Convert(database).ToList();
            return ev;
        }
    }
}
