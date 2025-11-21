using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Format
{
    public abstract class RoundFormat2 : RoundFormat
    {
        public RoundFormat2(EventManager em, Stage stage) : base(em, stage)
        {
        }

        public virtual IEnumerable<Race> CreateRaces(IEnumerable<Race> preExisting, Round round, int totalRacesCount)
        {
            if (totalRacesCount < 0)
                return Enumerable.Empty<Race>();

            List<Race> races = new List<Race>();
            races.AddRange(preExisting.OrderBy(r => r.RaceOrder));

            while (races.Count < totalRacesCount)
            {
                races.Add(new Race(EventManager.Event));
            }

            while (races.Count > totalRacesCount)
            {
                races.Remove(races.Last());
            }

            int i = 1;
            foreach (Race race in races)
            {
                race.RaceNumber = i;
                race.Round = round;
                i++;
            }

            return races;
        }


        public override IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            int raceCount = GetRaceCount(db, newRound, plan);

            IEnumerable<Race> races = CreateRaces(preExisting, newRound, raceCount);

            SetupRaces(db, races, plan);

            return races;   
        }


        public abstract int GetRaceCount(IDatabase db, Round newRound, RoundPlan plan);
        public virtual void SetupRaces(IDatabase db, IEnumerable<Race> races, RoundPlan plan)
        {
            foreach (Race race in races)
            {
                SetupRace(db, race, plan);
            }
        }

        public virtual void SetupRace(IDatabase db, Race races, RoundPlan plan) { }
    }
}
