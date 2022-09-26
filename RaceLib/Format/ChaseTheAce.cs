using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Format
{
    public class ChaseTheAce : RoundFormat
    {
        public ChaseTheAce(EventManager em)
           : base(em)
        {
        }

        public override IEnumerable<Race> GenerateRound(Database db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            Race[] races = RaceManager.GetRaces(plan.CallingRound).ToArray();

            foreach (var group in races.GroupByBracket())
            {
                Pilot[] pilots = group.GetPilots().ToArray();



            }
            return null;
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
