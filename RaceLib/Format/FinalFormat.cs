using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Format
{
    public class FinalFormat : RoundFormat
    {
        public ResultManager PointsManager { get { return EventManager.ResultManager; } }

        public FinalFormat(EventManager eventManager)
            :base(eventManager)
        {
        }

        public override IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            newRound.Stage.StageType = StageTypes.Final;
            db.Upsert(newRound);

            List<Race> races = new List<Race>();

            Race[] lastRoundRaces = EventManager.RaceManager.Races.Where(r => r.Round == plan.CallingRound).ToArray();
            Pilot[] Pilots = plan.Pilots;

            int heats = plan.NumberOfRaces;
            int startNumber = preExisting.Count();
            int maxPilotsPerRace = EventManager.GetMaxPilotsPerRace();
            
            for (int i = startNumber; i < heats; i++)
            {
                Race race = new Race(EventManager.Event);
                race.RaceNumber = startNumber + 1 + i;
                race.Round = newRound;
                race.Bracket = Brackets.A + i;
                races.Add(race);
            }

            if (!races.Any())
                return races;

            int raceIndex = 0;

            Pilot[] ordered = Pilots.OrderBy(p => lastRoundRaces.GetBracket(p)).ThenByDescending(p => PointsManager.GetPointsTotal(plan.CallingRound, p)).ThenBy(p => EventManager.LapRecordManager.GetPBTimePosition(p)).ToArray();
            int pilotsRemaining = ordered.Length;
            foreach (Pilot pilot in ordered)
            {
                Race race = races[raceIndex];
                IEnumerable<Channel> freeChannels = race.GetFreeFrequencies(EventManager.Channels);
                if (freeChannels.Count() == 0 || race.PilotCount >= maxPilotsPerRace)
                {
                    raceIndex++;

                    if (races.Count <= raceIndex)
                    {
                        break;
                    }
                    race = races[raceIndex];

                    maxPilotsPerRace = (int)Math.Ceiling((float)pilotsRemaining / (heats - raceIndex));
                }

                Channel c = pilot.GetChannelInRound(lastRoundRaces, plan.CallingRound);
                if (!race.IsFrequencyFree(c))
                {
                    c = freeChannels.OrderByDescending(ca => ca == c).FirstOrDefault();
                }

                race.SetPilot(db, c, pilot);
                pilotsRemaining--;
            }

            return races;
        }
    }
}
