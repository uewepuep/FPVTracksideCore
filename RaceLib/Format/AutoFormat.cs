using System;
using System.Collections.Generic;
using System.Linq;

namespace RaceLib.Format
{
    public class AutoFormat : RoundFormat
    {
        public AutoFormat(EventManager em) 
            : base(em)
        {
        }

        public override IEnumerable<Race> GenerateRound(IDatabase db, IEnumerable<Race> preExisting, Round newRound, RoundPlan plan)
        {
            Tools.Logger.Generation.LogCall(this, newRound, plan);
            List<Race> races = preExisting.Where(r => !r.Ended).ToList();

            Race[] lastRoundRaces;
            Brackets[] brackets;
            if (plan.CallingRound == null)
            {
                lastRoundRaces = new Race[0]; 
                brackets = new Brackets[] { Brackets.None };
            }
            else
            {
                lastRoundRaces = RaceManager.GetRaces(plan.CallingRound).ToArray();
                brackets = lastRoundRaces.GetBrackets().ToArray();
            }

            if (!brackets.Any())
            {
                brackets = new Brackets[] { Brackets.None };
            }
            int raceNumber = preExisting.Count();
            FlownMap flownMap = new FlownMap(RaceManager.Races);

            foreach (Brackets bracket in brackets)
            {
                Pilot[] lastRoundPilots = lastRoundRaces.Where(r => r.Bracket == bracket).GetPilots().ToArray();

                IEnumerable<Pilot> preExistingPilots = preExisting.SelectMany(r => r.Pilots);

                if (!lastRoundPilots.Any())
                {
                    lastRoundPilots = plan.Pilots.ToArray();
                }

                List<Pilot> allPilots = lastRoundPilots.Where(p => !p.PracticePilot && plan.Pilots.Contains(p)).ToList();
                int totalPilotCount = allPilots.Count;

                if (!allPilots.Any())
                {
                    continue;
                }

                Dictionary<Pilot, Channel> pilotChannels = allPilots.ToDictionary(p => p, p => EventManager.GetChannel(p));
                Dictionary<Pilot, Channel> lastHeatChannels = pilotChannels.ToDictionary(p => p.Key, p => p.Value);

                foreach (Pilot p in allPilots)
                {
                    Channel c = p.GetChannelInRound(RaceManager.Races, plan.CallingRound);

                    if (c != Channel.None && c != null)
                    {
                        pilotChannels[p] = c;
                        lastHeatChannels[p] = c;
                    }
                }

                List<Pilot> pilots = allPilots.Except(preExistingPilots).ToList();
                if (!pilots.Any())
                {
                    continue;
                }

                int heats = plan.NumberOfRaces;

                if (plan.NumberOfRaces == 0 || plan.AutoNumberOfRaces)
                {
                    heats = (int)Math.Ceiling(totalPilotCount / (float)plan.Channels.Count());
                }

                if (plan.ChannelChange == RoundPlan.ChannelChangeEnum.Change)
                {
                    IEnumerable<BandType> bandTypes = plan.Channels.Select(c => c.Band.GetBandType()).Distinct();
                    foreach (BandType bandType in bandTypes)
                    {
                        // Filter the pilots and the channels
                        Pilot[] pilotsOrderByUnflown = pilotChannels.Where(pc => pc.Value.Band.GetBandType() == bandType).OrderBy(kvp => flownMap.FlownPilotsSum(kvp.Key)).Select(kvp => kvp.Key).ToArray();
                        Channel[] btchannels = plan.Channels.Where(c => c.Band.GetBandType() == bandType).ToArray();

                        foreach (Pilot pilot in pilotsOrderByUnflown)
                        {
                            pilotChannels[pilot] = Channel.None;
                        }

                        foreach (Pilot pilot in pilotsOrderByUnflown)
                        {
                            Channel oldChannel = lastHeatChannels[pilot];

                            // Limit the channels this pilot should fly on..
                            IEnumerable<Pilot> unflown = flownMap.UnflownPilots(pilot, pilotsOrderByUnflown);
                            IEnumerable<Channel> unflownChannels = unflown.Select(a => pilotChannels[a]);
                            IEnumerable<Channel> flyableChannels = btchannels.Except(unflownChannels);

                            // Count how many times each channel is being used.
                            Dictionary<Channel, int> channelCount = btchannels.ToDictionary(b => b, b => 0);
                            foreach (var pc in pilotChannels)
                            {
                                foreach (var cc in btchannels)
                                {
                                    if (pc.Value.InterferesWith(cc))
                                    {
                                        channelCount[cc] += 1;
                                    }
                                }
                            }

                            if (flyableChannels.Any())
                            {
                                Channel ca = flyableChannels.OrderBy(c => channelCount[c]).ThenByDescending(c => c == oldChannel).First();
                                if (channelCount[ca] < heats)
                                {
                                    pilotChannels[pilot] = ca;
                                }
                            }

                            if (channelCount.Any())
                            {
                                if (pilotChannels[pilot] == Channel.None)
                                {
                                    pilotChannels[pilot] = channelCount.OrderBy(cc => cc.Value).ThenByDescending(cc => cc.Key == oldChannel).First().Key;
                                }
                            }
                        }
                    }
                }

                if (plan.NumberOfRaces == 0 || plan.AutoNumberOfRaces)
                {
                    int sharedChannels = HeatCountFromSharedFrequencies(pilotChannels);

                    int existingHeatsPerRound = RaceManager.GetRaceCount(plan.CallingRound, bracket);
                    heats = Math.Max(sharedChannels, Math.Max(existingHeatsPerRound, heats));
                }

                heats -= preExisting.Count();

                for (int i = 0; i < heats; i++)
                {
                    Race race = new Race(EventManager.Event);
                    race.RaceNumber = raceNumber + 1;
                    race.Round = newRound;
                    race.Bracket = bracket;
                    races.Add(race);
                    raceNumber++;
                }
                
                Race[] bracketRaces = races.Where(r => r.Bracket == bracket).ToArray();

                // Don't add pilots to casual practices.
                if (newRound.EventType == EventTypes.CasualPractice)
                {
                    continue;
                }

                int maxPerRace = (int)Math.Ceiling(totalPilotCount / (float)bracketRaces.Length);
                
                List<Pilot> toAllocate = pilots.ToList();

                // Arbitary limit on how many times to try and place a pilot..
                int maxLoops = toAllocate.Count * 4;

                int j = 0;
                while (toAllocate.Any() && j < maxLoops)
                {
                    j++;

                    List<PilotPoints> pilotPoints = new List<PilotPoints>();
                    foreach (Race r in bracketRaces)
                    {
                        foreach (Pilot p in toAllocate)
                        {
                            PilotPoints a = PointsForRace(flownMap, r, bracketRaces, p, pilotChannels[p], pilots, plan);
                            pilotPoints.Add(a);
                        }
                    }

                    foreach (PilotPoints pilotPoint in pilotPoints.OrderByDescending(pa => pa.Points))
                    {
                        Race race = pilotPoint.Race;
                        Pilot pilot = pilotPoint.Pilot;
                        Channel channel = pilotChannels[pilot];
                        BandType bandType = BandType.Analogue;

                        if (channel != null)
                        {
                            bandType = channel.Band.GetBandType();
                        }

                        bool set = false;
#if DEBUG
                        Tools.Logger.Generation.LogCall(this, pilot, "picked", pilotPoint.Points, "flo " + flownMap.FlownPilotCount(pilot), "unf " + flownMap.UnflownPilots(pilot, pilots).Count());
#endif
                        if (!race.IsFrequencyFree(channel) || !plan.Channels.Contains(channel))
                        {
                            // find a channel
                            Channel newChannel = RaceManager.GetFreeChannel(race, bandType, plan.Channels);

                            if (newChannel == null)
                            {
                                // Fall back, just any channel, hopefully doesnt; happen much :(
                                newChannel = plan.Channels.Except(race.Channels).Where(c => c.Band.GetBandType() == bandType).OrderByDescending(ca => ca == channel).FirstOrDefault();
                            }

                            IEnumerable<Race> allRaces = EventManager.RaceManager.Races.Where(r => r.Type == newRound.EventType);
                            Pilot pilotOnChannel = race.GetPilot(channel);
                            if (pilotOnChannel != null && pilot.CountChannelChanges(allRaces) > pilotOnChannel.CountChannelChanges(allRaces))
                            {
                                race.RemovePilot(db, pilotOnChannel);
                                race.SetPilot(db, newChannel, pilotOnChannel);
                                pilotChannels[pilotOnChannel] = newChannel;
                                set = true;
                            }
                            else
                            {
                                pilotChannels[pilot] = newChannel;
                                channel = newChannel;
                            }
                        }

                        if (race.IsFrequencyFree(channel) && plan.Channels.Contains(channel))
                        {
                            race.SetPilot(db, channel, pilot);
                            set = true;
                        }

                        if (set)
                        {
                            toAllocate.Remove(pilot);
                            break;
                        }
                    }
                }

                Race biggest = bracketRaces.OrderBy(br => br.PilotCount).Last();
                Race smallest = bracketRaces.OrderBy(br => br.PilotCount).First();

                // Even out the numbers of pilots. This takes priority over better matches
                while (biggest != null && smallest != null && (biggest.PilotCount - smallest.PilotCount > 1))
                {
                    List<PilotPoints> pilotPoints = new List<PilotPoints>();

                    foreach (Pilot p in biggest.Pilots)
                    {
                        PilotPoints newPoints = PointsForRace(flownMap, smallest, new Race[0], p, pilotChannels[p], pilots, plan);
                        PilotPoints oldPoints = PointsForRace(flownMap, biggest, new Race[0], p, pilotChannels[p], pilots, plan);

                        // remove the old points first, incase we had a really good match before.
                        newPoints.Points -= oldPoints.Points;

                        pilotPoints.Add(newPoints);
                    }

                    PilotPoints pilotPoint = pilotPoints.OrderByDescending(pa => pa.Points).First();
                    biggest.RemovePilot(db, pilotPoint.Pilot);
                    smallest.SetPilot(db, pilotChannels[pilotPoint.Pilot], pilotPoint.Pilot);

                    biggest = bracketRaces.OrderBy(br => br.PilotCount).Last();
                    smallest = bracketRaces.OrderBy(br => br.PilotCount).First();
                }

#if DEBUG
                Tools.Logger.Generation.Log(this, bracket.ToString() + "  #######################################################");

                foreach (Pilot p in lastRoundPilots)
                {
                    IEnumerable<Race> previousRaces = EventManager.RaceManager.Races.Concat(races).Where(r => r.Type == newRound.EventType && r.HasPilot(p));
                    IEnumerable<Pilot> previouslyRacedPilots = previousRaces.SelectMany(r => r.Pilots);
                    IEnumerable<Pilot> pilotsToRace = EventManager.Event.Pilots.Except(previouslyRacedPilots);

                    string output = " to fly " + pilotsToRace.Count() + " (" + string.Join(", ", pilotsToRace.Select(pa => pa.Name)) + ")";

                    IEnumerable<KeyValuePair<Pilot, int>> pilotsOverflown = flownMap.GetOverFlown(p);

                    output += " overflown (" + string.Join(", ", pilotsOverflown.Select(pa => pa.Key.Name + "(" +  pa.Value + ")")) + ")";

                    Tools.Logger.Generation.Log(this, p.ToString(), output);
                }

                Tools.Logger.Generation.Log(this, "############################################################");
#endif
            }

            IEnumerable<Race> newRaces = races.Except(preExisting);
            return newRaces;
        }
        
        private PilotPoints PointsForRace(FlownMap flownMap, Race race, IEnumerable<Race> racepool, Pilot p, Channel pilotChannel, List<Pilot> pilots, RoundPlan plan)
        {
            Pilot[] pilotsToRace = flownMap.UnflownPilots(p, pilots).ToArray();

            float points = 0;

            // If we're not already in the race...
            if (race.GetPilot(pilotChannel) != p)
            {
                // if its the only race with a free channel
                if (race.IsFrequencyFree(pilotChannel))
                {
                    if (!racepool.Where(r => r != race && r.IsFrequencyFree(pilotChannel)).Any())
                    {
                        points += 1000;
                    }
                }

                // is race full?
                points -= (race.PilotCount == plan.Channels.Length) ? 10000 : 0;

                // is Channel Free
                if (!race.IsFrequencyFree(pilotChannel) && plan.Channels.Contains(pilotChannel))
                {
                    if (plan.ChannelChange != RoundPlan.ChannelChangeEnum.Change)
                    {
                        points -= 1000;
                    }
                }
            }

            if (race.Pilots.Any())
            {
                int intersectionCount = pilotsToRace.Intersect(race.Pilots).Count();
                points += intersectionCount * intersectionCount * 5;

                // intersections with raced pilots
                points += flownMap.FlownPilotsSumSqr(p, race.Pilots) * -2f;
            }

            // Give pilots with more other pilots to race a better weighted chance of going next..
            points += pilotsToRace.Length;

            return new PilotPoints() { Pilot = p, Points = points, Race = race };
        }

        private class PilotPoints
        {
            public Pilot Pilot { get; set; }
            public float Points { get; set; }
            public Race Race { get; set; }

            public override string ToString()
            {
                return Race.ToString() + " " + Pilot.ToString() + " " + Points;
            }
        }
    }
}
