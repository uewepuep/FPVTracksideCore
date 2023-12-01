using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.Lite
{
    public class Result : DatabaseObjectT<RaceLib.Result>
    {
        public int Points { get; set; }
        public int Position { get; set; }

        public bool Valid { get; set; }

        [DBRef("Event")]
        public Event Event { get; set; }

        [DBRef("Pilot")]
        public Pilot Pilot { get; set; }

        [DBRef("Race")]
        public Race Race { get; set; }

        [DBRef("Round")]
        public Round Round { get; set; }

        public bool DNF { get; set; }

        public string ResultType { get; set; }


        public Result() { }

        public Result(RaceLib.Result obj)
            : base(obj)
        {
            if (obj.Event != null)
                Event = obj.Event.Convert<Event>();
            if (obj.Pilot != null)
                Pilot = obj.Pilot.Convert<Pilot>();
            if (obj.Race != null)
                Race = obj.Race.Convert<Race>();
            if (obj.Round != null)
                Round = obj.Round.Convert<Round>();
        }

        public override RaceLib.Result GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Result result = base.GetRaceLibObject(database);

            if (Event != null)
                result.Event = Event.Convert(database);
            if (Pilot != null)
                result.Pilot = Pilot.Convert(database);
            if (Race != null)
                result.Race = Race.Convert(database);
            if (Round != null)
                result.Round = Round.Convert(database);

            return result;
        }
    }
}
