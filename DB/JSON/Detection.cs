using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace DB.JSON
{
    public class Detection : DatabaseObjectT<RaceLib.Detection>
    {
        public int TimingSystemIndex { get; set; }

        public Guid Channel { get; set; }
        public DateTime Time { get; set; }

        public int Peak { get; set; }

        public string TimingSystemType { get; set; }

        public Guid Pilot { get; set; }

        public int LapNumber { get; set; }
        public bool Valid { get; set; }

        public string ValidityType { get; set; }

        public bool IsLapEnd { get; set; }

        public Detection() { }

        public Detection(RaceLib.Detection obj)
            : base(obj)
        {
            if (obj.Channel != null)
                Channel = obj.Channel.ID;
            if (obj.Pilot != null)
                Pilot = obj.Pilot.ID;
        }

        public override RaceLib.Detection GetRaceLibObject(IDatabase database)
        {
            RaceLib.Detection detection = base.GetRaceLibObject(database);
            detection.Pilot = Pilot.Convert<RaceLib.Pilot>(database);
            detection.Channel = Channel.Convert<RaceLib.Channel>(database);
            return detection;
        }
    }
}
