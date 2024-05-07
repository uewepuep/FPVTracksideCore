using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public static class Ext
    {
        public static T GetObject<T>(this IEnumerable<T> ts, Guid id) where T : BaseObject
        {
            return ts.FirstOrDefault(t => t.ID == id);
        }

        public static TimeSpan TotalTime(this IEnumerable<Lap> laps)
        {
            if (laps == null)
                return TimeSpan.MaxValue;

            if (!laps.Any())
            {
                return TimeSpan.MaxValue;
            }

            DateTime start = DateTime.MaxValue;
            DateTime end = DateTime.MinValue;

            foreach (Lap lap in laps)
            {
                if (start > lap.Start)
                    start = lap.Start;
                if (end < lap.End)
                    end = lap.End;
            }

            return end - start;
        }

        public static IEnumerable<Lap> BestConsecutive(this IEnumerable<Lap> laps, int consecutive)
        {
            IEnumerable<Lap> best = new Lap[0];

            Lap[] filtered = laps.Where(l => l.Detection.Valid && !l.Detection.IsHoleshot && l.Length.TotalSeconds > 0).OrderBy(l => l.End).ToArray();
            for (int i = 0; i <= filtered.Length - consecutive; i++)
            {
                IEnumerable<Lap> current = filtered.Skip(i).Take(consecutive);
                if (!best.Any() || current.TotalTime() < best.TotalTime())
                {
                    best = current;
                }
            }
            return best;
        }

        public static Lap Shortest(this IEnumerable<Lap> laps)
        {
            Lap shortest = laps.FirstOrDefault();
            if (shortest == null)
                return null;

            foreach (Lap lap in laps)
            {
                if (lap != null && lap.Length < shortest.Length)
                {
                    shortest = lap;
                }
            }

            return shortest;
        }

        public static string ToStringPosition(this int position)
        {
            string textPos = position.ToString();

            string post = "th";

            if (textPos.Length == 1 || textPos[textPos.Length - 2] != '1')
            {
                char lastChar = textPos.Last();
                switch (lastChar)
                {
                    case '1': post = "st"; break;
                    case '2': post = "nd"; break;
                    case '3': post = "rd"; break;
                }
            }
            return textPos + post;
        }

        public static char ToCharSign(this int diff)
        {
            return diff >= 0 ? '+' : '-';
        }

        public static bool HasResult(this EventTypes eventType)
        {
            switch (eventType)
            {
                case EventTypes.Race:
                case EventTypes.AggregateLaps:
                case EventTypes.TimeTrial:
                    return true;

                default:
                    return false;
            }
        }

        

        public static bool HasDelayedStart(this EventTypes eventType)
        {
            switch (eventType)
            {
                default:
                    return true;

                case EventTypes.Freestyle:
                case EventTypes.CasualPractice:
                    return false;
            }
        }

        public static bool HasPoints(this EventTypes eventType)
        {
            switch (eventType)
            {
                case EventTypes.Race:
                case EventTypes.AggregateLaps:
                    return true;

                default:
                    return false;
            }
        }

        public static bool HasLapCount(this EventTypes eventType)
        {
            switch (eventType)
            {
                case EventTypes.Race:
                    return true;

                default:
                    return false;
            }
        }

        public static string GetCharacter(this Band band)
        {
            if (band == Band.HDZero)
                return "Z";

            string bandName = Enum.GetName(typeof(Band), band);
            if (bandName.Length == 0)
                return "";
            return bandName[0].ToString();
        }

        public static bool AreAllType(this IEnumerable<Race> races, EventTypes type)
        {
            if (!races.Any())
            {
                return false;
            }

            foreach (Race race in races)
            {
                if (race.Type != type)
                {
                    return false;
                }
            }

            return true;
        }

        public static IEnumerable<IGrouping<Brackets, Race>> GroupByBracket(this IEnumerable<Race> races)
        {
            return races.GroupBy(r => r.Bracket);
        }

        public static IEnumerable<Race> OfBracket(this IEnumerable<Race> races, Brackets bracket)
        {
            return races.Where(r => r.Bracket == bracket);
        }

        public static Round GetRound(this IEnumerable<Race> races)
        {
            Round firstRound = races.Select(r => r.Round).FirstOrDefault();
            return firstRound;
        }

        public static IEnumerable<Race> GetRacesInRound(this IEnumerable<Race> races, int round)
        {
            return races.Where(r => r.RoundNumber == round);
        }

        public static IEnumerable<Brackets> GetBrackets(this IEnumerable<Race> races)
        {
            return races.Select(r => r.Bracket).Distinct();
        }

        public static IEnumerable<Pilot> GetPilots(this IEnumerable<Race> races)
        {
            return races.SelectMany(r => r.Pilots).Distinct();
        }

        public static Brackets GetBracket(this IEnumerable<Race> races, Pilot p)
        {
            return races.Where(r => r.HasPilot(p)).Select(r => r.Bracket).FirstOrDefault();
        }

        public static string ToString(this IEnumerable<Round> rounds)
        {
            string output = "";

            if (!rounds.Any())
            {
                return output;
            }

            Round lastRound = rounds.Last();
            EventTypes lastEventType = lastRound.EventType;

            string name = "";

            switch (lastRound.RoundType)
            {
                case Round.RoundTypes.Round:
                    name = RaceStringFormatter.Instance.GetEventTypeText(lastEventType);
                    break;
                case Round.RoundTypes.DoubleElimination:
                case Round.RoundTypes.Final:
                    name = lastRound.RoundType.ToString().CamelCaseToHuman();
                    break;
            }

            output += name + " Round ";

            List<int> numbers = new List<int>();
            foreach (Round round in rounds)
            {
                if (round.EventType == lastEventType)
                {
                    numbers.Add(round.RoundNumber);
                }
                else
                {
                    output += string.Join(", ", numbers.ToArray());
                    numbers.Clear();
                }
            }

            output += string.Join(", ", numbers.ToArray());

            if (lastRound.PointSummary != null)
            {
                List<string> strings = new List<string>();

                if (lastRound.PointSummary.DropWorstRound)
                    strings.Add("Drops worst round");
                if (lastRound.PointSummary.RoundPositionRollover && lastRound.RoundType == Round.RoundTypes.Final)
                {
                    strings.Add("Rolls over");
                }

                if (strings.Any())
                {
                    output += "\n- " + string.Join("\n- ", strings);
                }
            }

            return output;
        }

        public static BandType GetBandType(this Band band)
        {
            switch (band)
            {
                case Band.DJIFPVHD:
                    return BandType.DJIDigital;
                case Band.HDZero:
                    return BandType.HDZeroDigital;
                default:
                    return BandType.Analogue;
            }
        }

        public static Channel GetByShortString(this IEnumerable<Channel> channels, string shortString)
        {
            return channels.FirstOrDefault(c => c.ToStringShort() == shortString);
        }

        public static IEnumerable<Channel> GetOthersInChannelGroup(this IEnumerable<Channel> pool, Channel c)
        {
            foreach (var channelGroup in pool.GetChannelGroups())
            {
                if (channelGroup.Contains(c))
                {
                    foreach (Channel channel in channelGroup)
                    {
                        if (channel != c)
                        {
                            yield return channel;
                        }
                    }
                }
            }
        }

        public static Channel[] GetChannelGroup(this IEnumerable<Channel> pool, int index)
        {
            int i = 0;
            foreach (Channel[] channel in pool.GetChannelGroups())
            {
                if (i == index)
                    return channel;
                i++;
            }

            return null;
        }

        public static IEnumerable<Channel[]> GetChannelGroups(this IEnumerable<Channel> pool)
        {
            List<Channel> channels = pool.OrderBy(r => r.Frequency).ToList();
            while (channels.Any())
            {
                Channel next = channels.First();
                Channel[] interferring = next.GetInterferringChannels(channels).ToArray();
                if (interferring.Any())
                {
                    yield return interferring.ToArray();
                    channels.RemoveAll(r => interferring.Contains(r));
                }
            }
        }

        public static int CountBandTypes(this IEnumerable<Channel> pool, BandType bandType)
        {
            return pool.Count(r => r.Band.GetBandType() == bandType);
        }

        public static void ClearPilots(this IEnumerable<Race> races, IDatabase db) 
        {
            foreach (Race race in races)
            {
                race.ClearPilots(db);
            }
        }
    }
}
