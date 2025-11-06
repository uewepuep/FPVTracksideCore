using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib.Format
{
    public class StreetLeague : RoundFormat
    {
        public enum PointsStyle
        {
            PerHeat = 0,
            PerRound
        }

        public ResultManager ResultManager { get => EventManager.ResultManager; }


        public StreetLeague(EventManager em, Stage stage) 
            : base(em, stage)
        {
        }

        public override IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            Round lastRound = EventManager.RoundManager.PreviousRound(newRound);
            IEnumerable<Race> races = EventManager.RaceManager.GetRaces(lastRound);

            List<Race> newRaces = new List<Race>();
            var Pilots = new List<Pilot>();

            foreach (Race race in races)
            {
                foreach (var pilot in race.Pilots)
                {
                    Pilots.Add(pilot);
                }
            }

            // Sort pilots into races based on their event total points, lowest points in earliest rounds
            // For uneven groups push empty spots to higher groups
            var pilotPoints = ResultManager.Results.GroupBy(r => r.Pilot).Select(r => new { pilot = r.Key, points = r.Sum(r => r.Points) }).OrderBy(r => r.points).ToList();
            var groupBalance = GetGroupBalance(pilotPoints.Count, races.First().Channels.Length);
            var sampleRace = races.First();

            int raceIndex = 0;
            foreach (var group in groupBalance)
            {
                Race r = preExisting.GetAtIndex(raceIndex);
                if (r == null)
                {
                    r = sampleRace.Clone();
                    newRaces.Add(r);
                }
                r.RaceNumber = raceIndex + 1;
                r.Round = newRound;
                r.PilotChannels.Clear();
                foreach (var pc in EventManager.Event.Channels)
                {
                    r.PilotChannels.Add(new PilotChannel(null, pc));
                }

                List<Pilot> unassignedPilots = new List<Pilot>();
                for (int i = (group.Count - 1); i > -1; i--)
                {
                    var pilot = pilotPoints.ElementAt(group[i]).pilot;
                    var lastRace = races.First(e => e.Pilots.Contains(pilot));
                    var lastChannel = lastRace.PilotChannels.First(e => e.Pilot == pilot);
                    var currentChannel = r.PilotChannels.First(e => e.Channel == lastChannel.Channel);
                    if (currentChannel.Pilot == null)
                    {
                        currentChannel.Pilot = pilot;
                    }
                    else
                    {
                        unassignedPilots.Add(pilot);
                    }
                }
                while (unassignedPilots.Count > 0)
                {
                    var unassignedPilot = unassignedPilots.ElementAt(0);
                    foreach (var pc in r.PilotChannels)
                    {
                        if (pc.Pilot == null)
                        {
                            pc.Pilot = unassignedPilot;
                            unassignedPilots.RemoveAt(0);
                            break;
                        }
                    }
                }

                r.PilotChannels.RemoveAll(e => e.Pilot == null);
                raceIndex++;
            }

            foreach (Race r in newRaces)
            {
                RaceManager.AddRace(r);
            }

            return newRaces;
        }

        public List<List<int>> GetGroupBalance(int entries, int groupSize)
        {
            List<int> bigGroup = new List<int>();
            for (int i = 0; i < entries; i++)
            {
                bigGroup.Add(i);
            }

            List<List<int>> chunks = new List<List<int>>();
            for (int i = 0; i < bigGroup.Count; i += groupSize)
            {
                var chunk = new List<int>();
                for (int j = i; j < i + groupSize; j++)
                {
                    if (j < bigGroup.Count)
                    {
                        var element = bigGroup.ElementAtOrDefault(j);
                        chunk.Add(element);
                    }
                }
                chunks.Add(chunk);
            }

            if (chunks.Count > 1)
            {
                // Index of last but one
                var lastChunkTakenFrom = chunks.Count - 2;

                // while the graph list of the last chunk minus the graph list of the first is > 1
                while (Math.Abs(chunks[chunks.Count - 1].Count - chunks[0].Count) > 1)
                {
                    // move a graph from the last but one chunk to the last chunk
                    var movementChunk = chunks[lastChunkTakenFrom][chunks[lastChunkTakenFrom].Count - 1];
                    chunks[lastChunkTakenFrom].RemoveAt(chunks[lastChunkTakenFrom].Count - 1);
                    chunks[chunks.Count - 1].Add(movementChunk);

                    lastChunkTakenFrom--;

                    // set back to last but one index
                    if (lastChunkTakenFrom < 0)
                        lastChunkTakenFrom = chunks.Count - 2;
                }

            }

            //Clean up the numbers
            var fixedIndex = 0;
            for (var i = 0; i < chunks.Count; i++)
            {
                for (var k = 0; k < chunks[i].Count; k++)
                {
                    chunks[i][k] = fixedIndex;
                    fixedIndex++;
                }
            }

            return chunks;
        }

        public override void AdjustResults(Race race, IEnumerable<Result> results)
        {
            Round round = race.Round;

            var RoundResults = results.Where(r => r.Round == round).OrderBy(r => r.LapsFinished).ThenByDescending(r => r.Time).ToArray();
            for (int i = 0; i < RoundResults.Count(); i++)
            {
                RoundResults[i].Points = i + 1;
            }
        }

    }
}
