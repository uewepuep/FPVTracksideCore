using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace DB
{
    public class Round : DatabaseObjectT<RaceLib.Round>
    {
        public string Name { get; set; }

        public int RoundNumber { get; set; }

        public string EventType { get; set; }

        public bool Valid { get; set; }

        public int Order { get; set; }

        public DateTime ScheduledStart { get; set; }

        public string GameTypeName { get; set; }

        public Guid Stage { get; set; }

        public Round() { }

        public Round(RaceLib.Round obj)
            : base(obj)
        {
            if (obj.Stage != null)
                Stage = obj.Stage.ID;
        }

        public override RaceLib.Round GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Round round = base.GetRaceLibObject(database);
            round.Stage = Stage.Convert<RaceLib.Stage>(database);
            return round;
        }
    }
}
