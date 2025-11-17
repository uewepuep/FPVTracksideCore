using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Format
{

    public enum PilotOrdering
    {
        Default,
        MinimisePreviouslyFlown,
        Ordered,
        Seeded,
        Random,
    }

    public class RoundPlan
    {
        public enum ChannelChangeEnum
        {
            Change,
            KeepFromPreviousRound
        }
        [Browsable(false)]
        public Round CallingRound { get; set; }

        [Category("Races")]
        public bool AutoNumberOfRaces { get; set; }

        [Category("Races")]
        public int NumberOfRaces { get; set; }

        [Category("Races")]
        public PilotOrdering PilotSeeding { get; set; }

        [Category("Channels")]
        public ChannelChangeEnum ChannelChange { get; set; }

        [Category("Channels")]
        public Channel[] Channels { get; set; }

        [Category("Pilots")]
        public Pilot[] Pilots { get; set; }

        [Category("Stage")]
        [Browsable(false)]
        public bool KeepStage { get; set; }

        [Category("Stage")]
        [Browsable(false)]
        public Stage Stage { get; set; }

        public EventTypes EventType { get; set; }


        public RoundPlan(EventManager eventManager, Round previousRound, Stage stage, IEnumerable<Pilot> orderedPilots)
            :this(eventManager, previousRound, stage)
        {
            Pilots = orderedPilots.ToArray();
        }

        public RoundPlan(EventManager eventManager, Round previousRound, Stage stage)
        {
            EventType = eventManager.Event.EventType;
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
            PilotSeeding = PilotOrdering.MinimisePreviouslyFlown;
            Stage = stage;
            KeepStage = true;
        }
    }
}
