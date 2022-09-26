using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public delegate void PilotChannelDelegate(PilotChannel pilot);

    public class PilotChannel : BaseDBObject
    {
        [LiteDB.BsonRef("Pilot")]
        public Pilot Pilot { get; set; }
        
        [LiteDB.BsonRef("Channel")]
        public Channel Channel { get; set; }

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
