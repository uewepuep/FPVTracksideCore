using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Format
{
    public class DoubleElimination : RoundFormat
    {
        private int startNumber;

        public DoubleElimination(EventManager em)
           : base(em)
        {
        }

        public override IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            newRound.RoundType = Round.RoundTypes.DoubleElimination;
            db.Upsert(newRound);

            preExisting.ClearPilots(db);

            startNumber = preExisting.Count();

            List<Pilot> winners = new List<Pilot>();
            List<Pilot> losers = new List<Pilot>();

            Pilot[] alreadyPlaced = preExisting.GetPilots().ToArray();

            IEnumerable<Race> lastRoundRaces = EventManager.RaceManager.GetRaces(plan.CallingRound);

            int winnerRacePilots = lastRoundRaces.Where(r => r.Bracket != Brackets.Losers).GetPilots().Count();
            int loserRacePilots = lastRoundRaces.Where(r => r.Bracket == Brackets.Losers).GetPilots().Count();

            int totalWinnerSpots = (int)Math.Ceiling(winnerRacePilots / 2.0f);
            int newLoserSpots = winnerRacePilots - totalWinnerSpots;

            int loserWinnerSpots = (int)Math.Ceiling(loserRacePilots / 2.0f);

            int totalLoserSpots = loserWinnerSpots + newLoserSpots;
            List<Race> newRaces = new List<Race>();

            // Make the winners/losers lists.
            MakeWinnersLosers(lastRoundRaces, ref winners, ref losers);

            if (totalLoserSpots + totalWinnerSpots < plan.Channels.Length)
            {
                foreach (Race race in CreateRaces(totalWinnerSpots, preExisting, newRound, Brackets.None, plan))
                {
                    newRaces.Add(race);
                    startNumber++;
                }

                IEnumerable<Race> union = preExisting.Union(newRaces);
                IEnumerable<Pilot> allPilots = winners.Union(losers);

                AssignPilots(db, union, lastRoundRaces, allPilots.ToList(), Brackets.None, plan);
                return newRaces;
            }

            foreach (Race race in CreateRaces(totalWinnerSpots, preExisting, newRound, Brackets.Winners, plan))
            {
                newRaces.Add(race);
                startNumber++;
            }

            foreach (Race race in CreateRaces(totalLoserSpots, preExisting, newRound, Brackets.Losers, plan))
            {
                newRaces.Add(race);
                startNumber++;
            }

            IEnumerable<Race> allRaces = preExisting.Union(newRaces);

            AssignPilots(db, allRaces, lastRoundRaces, winners.Except(alreadyPlaced), Brackets.Winners, plan);
            AssignPilots(db, allRaces, lastRoundRaces, losers.Except(alreadyPlaced), Brackets.Losers, plan);

            return newRaces;
        }

        private IEnumerable<Race> CreateRaces(int totalPilots, IEnumerable<Race> preExisting, Round round, Brackets bracket, RoundPlan plan)
        {
            int channelCount = plan.Channels.GetChannelGroups().Count();
            int needed = (int)Math.Ceiling(totalPilots / (float)channelCount);

            int have = preExisting.OfBracket(bracket).Count();

            for (int i = have; i < needed; i++)
            {
                Race race = new Race(EventManager.Event);
                race.RaceNumber = startNumber + 1 + i;
                race.Round = round;
                race.Bracket = bracket;
                yield return race;
            }
        }

        private void AssignPilots(IDatabase db, IEnumerable<Race> allRoundRaces, IEnumerable<Race> previousRoundRaces, IEnumerable<Pilot> pilots, Brackets bracket, RoundPlan plan)
        {
            Race[] races = allRoundRaces.OfBracket(bracket).ToArray();

            // Order them by last race, effectively "rotating" them
            Pilot[] orderedPilots = pilots.OrderBy(p => GetLastRaceNumber(previousRoundRaces, plan.CallingRound, p)).ToArray();
            foreach (Pilot p in orderedPilots)
            {
                Race race = races.OrderBy(r => r.PilotCount).FirstOrDefault();
                if (race == null)
                    continue;

                Channel c = EventManager.GetChannel(p);

                Race lastRace = p.GetRaceInRound(previousRoundRaces, plan.CallingRound);
                if (lastRace != null)
                {
                    c = lastRace.GetChannel(p);
                }

                if (!race.IsFrequencyFree(c))
                {
                    c = race.GetFreeFrequencies(plan.Channels).FirstOrDefault();
                }

                if (c != null)
                {
                    race.SetPilot(db, c, p);
                }
            }
        }

        private int GetLastRaceNumber(IEnumerable<Race> races, Round lastRound, Pilot p)
        {
            Race race = races.Where(r => r.HasPilot(p) && r.Round == lastRound).OrderByDescending(r => r.RaceNumber).FirstOrDefault();
            if (race == null)
                return 0;
            return race.RaceNumber;
        }

        private bool TopHalf(Race race, Pilot pilot)
        {
            int topHalf = (int)Math.Ceiling(race.PilotCount / 2.0f);
            int position = EventManager.ResultManager.GetPosition(race, pilot);

            if (position <= 0)
                return false;

            return position <= topHalf;
        }

        public void MakeWinnersLosers(IEnumerable<Race> lastRoundRaces, ref List<Pilot> winners, ref List<Pilot> losers)
        {
            IEnumerable<Race> ended = lastRoundRaces.Where(r => r.Ended);
            foreach (Race race in ended)
            {
                if (race.Bracket == Brackets.Losers)
                {
                    foreach (Pilot p in race.Pilots)
                    {
                        if (TopHalf(race, p) && !losers.Contains(p))
                        {
                            losers.Add(p);
                        }
                    }
                }
                else
                {
                    foreach (Pilot p in race.Pilots)
                    {
                        if (TopHalf(race, p))
                        {
                            if (!winners.Contains(p))
                                winners.Add(p);
                        }
                        else
                        {
                            if (!losers.Contains(p))
                                losers.Add(p);
                        }
                    }
                }
            }
        }

        public override IEnumerable<Pilot> GetOutputPilots(Round round)
        {
            IEnumerable<Race> lastRoundRaces = EventManager.RaceManager.GetRaces(round);

            List<Pilot> winners = new List<Pilot>();
            List<Pilot> losers = new List<Pilot>();

            MakeWinnersLosers(lastRoundRaces, ref winners, ref losers);
            return winners.Union(losers);
        }
    }
}
