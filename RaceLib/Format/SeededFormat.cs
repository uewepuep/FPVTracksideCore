using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Format
{
    public class SeededFormat : RoundFormat
    {

        public SeededFormat(EventManager eventManager)
            : base(eventManager)
        {
        }

        public override IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            Pilot[] pilots = plan.Pilots.ToArray();

            List<Race> races = new List<Race>();

            Race[] lastRoundRaces = EventManager.RaceManager.Races.Where(r => r.Round == plan.CallingRound).ToArray();

            int heats = plan.NumberOfRaces;
            int startNumber = preExisting.Count();
            int maxPilotsPerRace = plan.Channels.Length;

            for (int i = 0; i < heats; i++)
            {
                Race race = new Race(EventManager.Event);
                race.RaceNumber = startNumber + 1 + i;
                race.Round = newRound;
                races.Add(race);
            }

            if (!races.Any())
                return races;

            int raceIndex = 0;

            foreach (Pilot pilot in pilots)
            {
                Race race = races[raceIndex];
                BandType bandType = BandType.Analogue;
                Channel c = pilot.GetChannelInRound(lastRoundRaces, plan.CallingRound);
                if (c != null)
                {
                    bandType = c.Band.GetBandType();
                }

                if (!race.IsFrequencyFree(c))
                {
                    IEnumerable<Channel> freeChannels = plan.Channels.Except(race.Channels).Where(r => r.Band.GetBandType() == bandType);
                    c = freeChannels.OrderByDescending(ca => ca == c).FirstOrDefault();
                }

                race.SetPilot(db, c, pilot);
                raceIndex = (raceIndex + 1) % races.Count;
            }

            return races;
        }
    }
}
