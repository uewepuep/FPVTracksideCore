using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Format
{
    public class RoundPlan
    {
        public enum ChannelChangeEnum
        {
            Change,
            KeepFromPreviousRound
        }

        public enum PilotOrderingEnum
        {
            MinimisePreviouslyFlown,
            Ordered,
            Seeded,
        }

        [Browsable(false)]
        public Round CallingRound { get; private set; }

        [Category("Races")]
        public bool AutoNumberOfRaces { get; set; }

        [Category("Races")]
        public int NumberOfRaces { get; set; }

        [Category("Races")]
        public PilotOrderingEnum PilotSeeding { get; set; }

        [Category("Channels")]
        public ChannelChangeEnum ChannelChange { get; set; }

        [Category("Channels")]
        public Channel[] Channels { get; set; }

        [Category("Pilots")]
        public Pilot[] Pilots { get; set; }

        public RoundPlan(EventManager eventManager, Round previousRound)
        {
            AutoNumberOfRaces = true;
            CallingRound = previousRound;
            Channels = eventManager.Channels;

            if (CallingRound == null)
            {
                Pilots = eventManager.Event.Pilots.ToArray();
                NumberOfRaces = RoundFormat.HeatCountFromSharedFrequencies(eventManager.Event.PilotChannels);
            }
            else
            {
                Pilots = eventManager.RoundManager.GetOutputPilots(previousRound).ToArray();
                NumberOfRaces = eventManager.RaceManager.GetRaces(previousRound).Count();
            }
            ChannelChange = ChannelChangeEnum.KeepFromPreviousRound;
            PilotSeeding = PilotOrderingEnum.MinimisePreviouslyFlown;
        }
    }
}
