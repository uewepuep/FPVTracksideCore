using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.Lite
{
    public class Lap : DatabaseObjectT<RaceLib.Lap>
    {
        public Detection Detection { get; set; }

        public TimeSpan Length { get; set; }

        public Lap() { }

        public Lap(RaceLib.Lap obj)
            : base(obj)
        {
            if (obj.Detection != null)
                Detection = obj.Detection.Convert<Detection>();
        }

        public override RaceLib.Lap GetRaceLibObject(IDatabase database)
        {
            RaceLib.Lap lap = base.GetRaceLibObject(database);
            lap.Detection = Detection.Convert(database);
            return lap;
        }
    }
}
