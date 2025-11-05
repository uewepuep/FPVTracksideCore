using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class Result : DatabaseObjectT<RaceLib.Result>
    {
        public int Points { get; set; }
        public int Position { get; set; }

        public bool Valid { get; set; }

        public Guid Event { get; set; }

        public Guid Pilot { get; set; }

        public Guid Race { get; set; }

        public Guid Round { get; set; }

        public bool DNF { get; set; }

        public string ResultType { get; set; }
        public TimeSpan Time { get; set; }
        public int LapsFinished { get; set; }

        public Result() { }

        public Result(RaceLib.Result obj)
            : base(obj)
        {
            if (obj.Event != null)
                Event = obj.Event.ID;
            if (obj.Pilot != null)
                Pilot = obj.Pilot.ID;
            if (obj.Race != null)
                Race = obj.Race.ID;
            if (obj.Round != null)
                Round = obj.Round.ID;
        }

        public override RaceLib.Result GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Result result = base.GetRaceLibObject(database);

            if (Event != null)
                result.Event = Event.Convert<RaceLib.Event>(database);
            if (Pilot != null)
                result.Pilot = Pilot.Convert<RaceLib.Pilot>(database);
            if (Race != null)
                result.Race = Race.Convert<RaceLib.Race>(database);
            if (Round != null)
                result.Round = Round.Convert<RaceLib.Round>(database);

            return result;
        }
    }
}
