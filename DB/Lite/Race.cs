using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.Lite
{
    public class Race : DatabaseObjectT<RaceLib.Race>
    {
        [DBRef("Lap")]
        public List<Lap> Laps { get; set; }

        [DBRef("Detection")]
        public List<Detection> Detections { get; set; }

        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public TimeSpan TotalPausedTime { get; set; }

        [DBRef("PilotChannel")]
        public List<PilotChannel> PilotChannels { get; set; }

        public int RaceNumber { get; set; }

        [DBRef("Round")]
        public Round Round { get; set; }

        public int TargetLaps { get; set; }

        public string PrimaryTimingSystemLocation { get; set; }

        public bool Valid { get; set; }

        public bool AutoAssignNumbers { get; set; }

        [DBRef("Event")]
        public Event Event { get; set; }

        public bool Uploaded { get; set; }

        public string Bracket { get; set; }

        public Race() { }

        public Race(RaceLib.Race obj)
            : base(obj)
        {
            if (obj.PilotChannels != null)
                PilotChannels = obj.PilotChannels.Convert<PilotChannel>().ToList();
            if (obj.Laps != null)
                Laps = obj.Laps.Convert<Lap>().ToList();
            if (obj.Detections != null)
                Detections = obj.Detections.Convert<Detection>().ToList();
            if (obj.Round != null)
                Round = obj.Round.Convert<Round>();
            if (obj.Event != null)
                Event = obj.Event.Convert<Event>();
        }

        public override RaceLib.Race GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Race race = base.GetRaceLibObject(database);

            race.Laps = Laps.Convert(database).ToList();
            race.PilotChannels = PilotChannels.Convert(database).ToList();
            race.Detections = Detections.Convert(database).ToList();

            race.Round = Round.Convert(database);

            // Back reference for lap to race.
            foreach (RaceLib.Lap lap in race.Laps)
            {
                lap.Race = race;
            }

            return race;
        }

    }
}
