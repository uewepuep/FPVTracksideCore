using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib.Format
{
    public class ChaseTheAce : RoundFormat
    {
        public int Limit { get; set; }


        public ChaseTheAce(EventManager em, Stage stage)
           : base(em, stage)
        {
            Limit = 2;
        }

        public override IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            if (preExisting.Any())
                yield break;

            Race[] races = RaceManager.GetRaces(Stage).OrderBy(r => r.RoundNumber).ThenBy(r => r.RaceOrder).ToArray();

            Dictionary<Pilot, int> wins = new Dictionary<Pilot, int>();
            foreach (Race r in races)
            {
                foreach (Pilot p in r.Pilots)
                {
                    int position = EventManager.ResultManager.GetPosition(r, p);
                    if (position == 1)
                    {
                        wins.SetValue(p, position);
                        break;
                    }
                }
            }

            int mostWins = wins.Values.Max();

            if (mostWins < Limit)
            {
                Race race = races.LastOrDefault();
                Race newRace = race.Clone();

                newRace.Round = newRound;
                newRace.RaceNumber = 1;

                yield return newRace;
            }
        }

        public static bool CanGenerate(RaceManager raceManager, Round round)
        {
            IEnumerable<Race> races = raceManager.GetRaces(round);

            if (races.Any(r => !r.Ended))
                return false;

            if (races.GroupByBracket().Any(g => g.Count() > 1))
                return false;

            return true;
        }
    }
}
