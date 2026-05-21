using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public delegate void PilotChannelDelegate(PilotChannel pilot);

    public class PilotChannel : BaseObject
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

        public virtual PilotChannel Clone()
        {
            // This is important, pilot channels cannot be shared between races, events, etc
            return new PilotChannel(Pilot, Channel);
        }
    }

    public class RacePilotChannel : PilotChannel
    {
        public TimeSpan HandicapOffset { get; set; }

        public RacePilotChannel()
        {
        }

        public RacePilotChannel(Pilot p, Channel c)
            : base(p, c)
        {
        }

        public override RacePilotChannel Clone()
        {
            return new RacePilotChannel(Pilot, Channel) { HandicapOffset = HandicapOffset };
        }
    }

    public static class PilotChannelExtensions
    {
        public static IEnumerable<PilotChannel> Clone(this IEnumerable<PilotChannel> pilotChannels)
        {
            return pilotChannels.Select(pc => pc.Clone());
        }

        public static IEnumerable<RacePilotChannel> Clone(this IEnumerable<RacePilotChannel> pilotChannels)
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
            return pilotChannels.FirstOrDefault(pc => pc != null && pc.Channel != null && pc.Channel.InterferesWith(channel));
        }

        public static RacePilotChannel Get(this IEnumerable<RacePilotChannel> pilotChannels, Channel channel)
        {
            return pilotChannels.FirstOrDefault(pc => pc != null && pc.Channel != null && pc.Channel.InterferesWith(channel));
        }

        public static PilotChannel Get(this IEnumerable<PilotChannel> pilotChannels, Pilot pilot)
        {
            return pilotChannels.FirstOrDefault(pc => pc.Pilot == pilot);
        }

        public static RacePilotChannel Get(this IEnumerable<RacePilotChannel> pilotChannels, Pilot pilot)
        {
            return pilotChannels.FirstOrDefault(pc => pc.Pilot == pilot);
        }
    }
}
