using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.Lite
{
    public class PilotChannel : DatabaseObjectT<RaceLib.PilotChannel>
    {
        [DBRef("Pilot")]
        public Pilot Pilot { get; set; }

        [DBRef("Channel")]
        public Channel Channel { get; set; }


        public PilotChannel() { }

        public PilotChannel(RaceLib.PilotChannel obj)
            : base(obj)
        {
            if (obj.Pilot != null)
                Pilot = obj.Pilot.Convert<Pilot>();

            if (obj.Channel != null)
                Channel = obj.Channel.Convert<Channel>();
        }

        public override RaceLib.PilotChannel GetRaceLibObject(RaceLib.IDatabase database)
        {
            RaceLib.PilotChannel pilotChannel = base.GetRaceLibObject(database);
            pilotChannel.Pilot = Pilot.Convert(database);
            pilotChannel.Channel = Channel.Convert(database);
            return pilotChannel;
        }
    }
}
