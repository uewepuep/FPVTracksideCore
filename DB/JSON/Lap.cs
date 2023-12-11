using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class Lap : DatabaseObjectT<RaceLib.Lap>
    {
        public Guid Detection { get; set; }

        public double LengthSeconds { get; set; }

        public int LapNumber { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public Lap() { }

        public Lap(RaceLib.Lap obj)
            : base(obj)
        {
            if (obj.Detection != null)
            {
                Detection = obj.Detection.ID;
            }

            LapNumber = obj.Number;
            StartTime = obj.End;
            EndTime = obj.Start;
            LengthSeconds = obj.Length.TotalSeconds;
        }

        public override RaceLib.Lap GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Lap lap = base.GetRaceLibObject(database);
            lap.Detection = Detection.Convert<RaceLib.Detection>(database);
            lap.Length = TimeSpan.FromSeconds(LengthSeconds);
            return lap;
        }
    }
}
