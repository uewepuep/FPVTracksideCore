using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class GamePoint : DatabaseObjectT<RaceLib.GamePoint>
    {
        public Guid Channel { get; set; }
        public Guid Pilot { get; set; }
        public bool Valid { get; set; }
        public DateTime Time { get; set; }

        public GamePoint() { }

        public GamePoint(RaceLib.GamePoint obj)
            : base(obj)
        {
            if (obj.Channel != null)
                Channel = obj.Channel.ID;
            if (obj.Pilot != null)
                Pilot = obj.Pilot.ID;
        }

        public override RaceLib.GamePoint GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.GamePoint detection = base.GetRaceLibObject(database);
            detection.Pilot = Pilot.Convert<RaceLib.Pilot>(database);
            detection.Channel = Channel.Convert<RaceLib.Channel>(database);
            return detection;
        }
    }
}
