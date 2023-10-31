using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib.Format
{
    public abstract class RoundFormat
    {
        public EventManager EventManager { get; private set; }
        public RaceManager RaceManager { get { return EventManager.RaceManager; } }

        public RoundFormat(EventManager em)
        {
            EventManager = em;
        }

        public abstract IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan);

        public virtual IEnumerable<Pilot> GetOutputPilots(Round round)
        {
            IEnumerable<Race> races = EventManager.RaceManager.GetRaces(round);
            return races.GetPilots().Distinct().ToArray();
        }

        public static int HeatCountFromSharedFrequencies(IEnumerable<PilotChannel> pilotChannels)
        {
            Dictionary<Pilot, Channel> dictionary = new Dictionary<Pilot, Channel>();
            foreach (var kvp in pilotChannels)
            {
                dictionary.Add(kvp.Pilot, kvp.Channel);
            }
            return HeatCountFromSharedFrequencies(dictionary);
        }

        public static int HeatCountFromSharedFrequencies(Dictionary<Pilot, Channel> pilotChannels)
        {
            if (!pilotChannels.Any())
            {
                return 0;
            }

            var grouped = pilotChannels.GroupBy(pc => pc.Value);
            Dictionary<Channel, int> counted = grouped.ToDictionary(g => g.Key, g => g.Count());

            int max = 0;
            foreach (var kvp in counted)
            {
                Channel channel = kvp.Key;

                int newMax = kvp.Value;

                IEnumerable<Channel> others = counted.Keys.Where(c => c != channel);
                IEnumerable<Channel> interferring = channel.GetInterferringChannels(others);
                foreach (Channel inter in interferring)
                {
                    int interMax;
                    if (counted.TryGetValue(inter, out interMax))
                    {
                        newMax += interMax;
                    }
                }

                max = Math.Max(max, newMax);
            }

            return max;
        }

    }
}
