using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Format
{
    public class FlownMap
    {
        private Dictionary<Pilot, Dictionary<Pilot, int>> pilotPilotFlownCount;

        public FlownMap(IEnumerable<Race> races)
            : this()
        {
            AddRaces(races);
        }

        public FlownMap()
        {
            pilotPilotFlownCount = new Dictionary<Pilot, Dictionary<Pilot, int>>();
        }

        public void AddRace(Race race)
        {
            if (race.Type == EventTypes.Practice)
            {
                return;
            }

            foreach (Pilot pilot in race.Pilots)
            {
                Dictionary<Pilot, int> thisPilotFlownCount;
                if (!pilotPilotFlownCount.TryGetValue(pilot, out thisPilotFlownCount))
                {
                    thisPilotFlownCount = new Dictionary<Pilot, int>();
                    pilotPilotFlownCount.Add(pilot, thisPilotFlownCount);
                }

                foreach (Pilot otherPilot in race.Pilots)
                {
                    if (otherPilot == pilot)
                        continue;

                    int count;
                    if (!thisPilotFlownCount.TryGetValue(otherPilot, out count))
                    {
                        count = 0;
                    }

                    count++;

                    thisPilotFlownCount[otherPilot] = count;
                }
            }
        }

        public void AddRaces(IEnumerable<Race> races)
        {
            foreach (Race race in races)
            {
                AddRace(race);
            }
        }

        public int GetFlownCount(Pilot pilot, Pilot otherPilot)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                int count;
                if (flownCount.TryGetValue(otherPilot, out count))
                {
                    return count;
                }
            }
            return 0;
        }

        public IEnumerable<Pilot> FlownPilots(Pilot pilot)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                return flownCount.Keys;
            }
            return new Pilot[0];
        }

        public IEnumerable<Pilot> FlownPilots(Pilot pilot, IEnumerable<Pilot> flown)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                return flownCount.Keys.Intersect(flown);
            }
            return new Pilot[0];
        }

        public int FlownPilotCount(Pilot pilot)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                return flownCount.Count;
            }
            return 0;
        }

        public int FlownPilotsSum(Pilot pilot)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                int sum = flownCount.Values.Sum();
                return sum;
            }
            return 0;
        }

        public int FlownPilotsSum(Pilot pilot, IEnumerable<Pilot> onlyThese)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                int sum = 0;
                foreach (Pilot other in onlyThese)
                {
                    int count;
                    if (flownCount.TryGetValue(other, out count))
                    {
                        sum += count;
                    }
                }
                return sum;
            }
            return 0;
        }

        public int FlownPilotsSumSqr(Pilot pilot, IEnumerable<Pilot> onlyThese)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                int sum = 0;
                foreach (Pilot other in onlyThese)
                {
                    int count;
                    if (flownCount.TryGetValue(other, out count))
                    {
                        sum += count * count;
                    }
                }
                return sum;
            }
            return 0;
        }

        public int FlownPilotsMax(Pilot pilot)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                if (flownCount.Any())
                {
                    int max = flownCount.Values.Max();
                    return max;
                }
            }
            return 0;
        }

        public IEnumerable<Pilot> UnflownPilots(Pilot pilot, IEnumerable<Pilot> otherPilots)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                return otherPilots.Except(flownCount.Keys);
            }
            return otherPilots;
        }

        public int UnflownPilotCount(Pilot pilot, IEnumerable<Pilot> otherPilots)
        {
            var pilots = UnflownPilots(pilot, otherPilots);
            return pilots.Count();
        }

        public IEnumerable<KeyValuePair<Pilot, int>> GetOverFlown(Pilot pilot)
        {
            Dictionary<Pilot, int> flownCount;
            if (pilotPilotFlownCount.TryGetValue(pilot, out flownCount))
            {
                return flownCount.Where(r => r.Value > 1);
            }

            return new KeyValuePair<Pilot, int>[0];
        }
    }
}
