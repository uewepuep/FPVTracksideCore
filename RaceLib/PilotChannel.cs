using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public delegate void PilotChannelDelegate(PilotChannel pilot);

    public class PilotChannel : BaseObjectT<DB.PilotChannel>
    {
        public Pilot Pilot { get; set; }
        
        public Channel Channel { get; set; }

        
        public string PilotName
        {
            get
            {
                if (Pilot == null) 
                {
                    return "";
                }
                return Pilot.Name;
            }
        }
        public PilotChannel(DB.PilotChannel obj)
            : base(obj)
        {
            Pilot = obj.Pilot.Convert<Pilot>();
            Channel = obj.Channel.Convert<Channel>();
        }


        public PilotChannel()
        {
        }

        public PilotChannel(Pilot p, Channel c)
        {
            Pilot = p;
            Channel = c;
        }

        public override string ToString()
        {
            return Pilot.ToString() + " " + Channel.ToString();
        }

        public PilotChannel Clone()
        {
            // This is important, pilot channels cannot be shared between races, events, etc
            return new PilotChannel(Pilot, Channel);
        }

        public override DB.PilotChannel GetDBObject()
        {
            DB.PilotChannel pilotChannel = base.GetDBObject();
            pilotChannel.Pilot = Pilot.GetDBObject();
            pilotChannel.Channel = Channel.GetDBObject();
            return pilotChannel;
        }
    }

    public static class PilotChannelExtensions
    {
        public static IEnumerable<PilotChannel> Clone(this IEnumerable<PilotChannel> pilotChannels)
        {
            return pilotChannels.Select(pc => pc.Clone());
        }

        public static bool Contains(this IEnumerable<PilotChannel> pilotChannels, Pilot pilot)
        {
            return pilotChannels.Any(pc => pc.Pilot == pilot);
        }

        public static bool Contains(this IEnumerable<PilotChannel> pilotChannels, Channel channel)
        {
            return pilotChannels.Any(pc => pc.Channel.InterferesWith(channel));
        }

        public static PilotChannel Get(this IEnumerable<PilotChannel> pilotChannels, Channel channel)
        {
            return pilotChannels.FirstOrDefault(pc =>  pc.Channel.InterferesWith(channel));
        }

        public static PilotChannel Get(this IEnumerable<PilotChannel> pilotChannels, Pilot pilot)
        {
            return pilotChannels.FirstOrDefault(pc => pc.Pilot == pilot);
        }
    }
}
