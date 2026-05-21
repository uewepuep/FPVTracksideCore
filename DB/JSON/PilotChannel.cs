using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class PilotChannel : DatabaseObjectT<RaceLib.PilotChannel>
    {
        public Guid Pilot { get; set; }

        public Guid Channel { get; set; }

        public PilotChannel() { }

        public PilotChannel(RaceLib.PilotChannel obj)
            : base(obj)
        {
            if (obj.Pilot != null)
                Pilot = obj.Pilot.ID;

            if (obj.Channel != null)
                Channel = obj.Channel.ID;
        }

        public override RaceLib.PilotChannel GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.PilotChannel pilotChannel = base.GetRaceLibObject(database);
            pilotChannel.Pilot = Pilot.Convert<RaceLib.Pilot>(database);
            pilotChannel.Channel = Channel.Convert<RaceLib.Channel>(database);
            return pilotChannel;
        }
    }

    public class RacePilotChannel : DatabaseObjectT<RaceLib.RacePilotChannel>
    {
        public Guid Pilot { get; set; }

        public Guid Channel { get; set; }

        public TimeSpan HandicapOffset { get; set; }

        public RacePilotChannel() { }

        public RacePilotChannel(RaceLib.RacePilotChannel obj)
            : base(obj)
        {
            if (obj.Pilot != null)
                Pilot = obj.Pilot.ID;

            if (obj.Channel != null)
                Channel = obj.Channel.ID;

            HandicapOffset = obj.HandicapOffset;
        }

        public override RaceLib.RacePilotChannel GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.RacePilotChannel pc = base.GetRaceLibObject(database);
            pc.Pilot = Pilot.Convert<RaceLib.Pilot>(database);
            pc.Channel = Channel.Convert<RaceLib.Channel>(database);
            pc.HandicapOffset = HandicapOffset;
            return pc;
        }
    }
}
