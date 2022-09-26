//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Sound.AutoCommentator
//{
//    public class Callouts
//    {

//        public static IEnumerable<Callout> GetCallouts()
//        {

//            Pilot stuff
//            yield return new Callout()
//            {
//                Trigger = Trigger.CrashesOut,
//                Alternatives = new string[]
//            {
//                "{pilot} lights out",
//                "{pilot} takes a tumble",
//                "{pilot} dead in the water",
//                "{pilot} has no video",
//                "{pilot} is now down"
//            }
//            };


//            yield return new Callout()
//            {
//                Trigger = Trigger.GetsFirst,
//                Alternatives = new string[]
//            {
//                "{pilot} moves up into the number 1 spot",
//                "{pilot} takes the lead",
//                "{pilot} is now leading this race",
//                "{pilot} is in first!"
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.GainsPosition,
//                Alternatives = new string[]
//            {
//                "{pilot} is moving up through the field, now in {position}",
//                "{pilot} overtakes {pilot2}",
//                "{pilot} is now in {position}"
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.CloseRace,
//                Alternatives = new string[]
//            {
//                "Only {time} separates our {count} pilots",
//                "It's a very close race"
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.FasterComparedToPrevious,
//                Alternatives = new string[]
//            {
//                "{pilot}'s last lap was faster than their previous lap"
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.JustBehind,
//                Alternatives = new string[]
//            {
//                "{pilot} is only {time} behind {pilot2}",
//                "It's anyones race between {pilot} and {pilot2}",
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.LargeLead,
//                Alternatives = new string[]
//            {
//                "{pilot} has a huge lead",
//                "{pilot} looks unstoppable in this race",
//                "This race is surely to be a win for {pilot}",
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.Win,
//                Alternatives = new string[]
//            {
//                "{pilot} wins it",
//                "{pilot} takes the win",
//                "First place for {pilot}",
//                "This race belongs to {pilot}"
//            }
//            };


//            yield return new Callout()
//            {
//                Trigger = Trigger.BehindLead,
//                Alternatives = new string[]
//            {
//                "{pilot} is {time} behind the leader",
//                "{pilot} is {time} off the pace",
//                "{pilot} has {time} to make up if they want to win this",
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.Consistent,
//                Alternatives = new string[]
//            {
//                "{pilot} putting in some consistent laps",
//                "Only {time} between {pilot}'s fastest and slowest laps",
//                "{pilot} flying smooth",
//            }
//            };


//            Race

//            yield return new Callout()
//            {
//                Trigger = Trigger.AfterRaceLotsOfPositionChanges,
//                Alternatives = new string[]
//            {
//                "Now that was an exciting race",
//                "Excellent racing everyone. That was a good one to watch."
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.RaceStart,
//                Alternatives = new string[]
//            {
//                "And we're off!",
//                "Good luck pilots",
//                "Weee!",
//                "The tone has gone off and we're racing"
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.AllOnLap,
//                Alternatives = new string[]
//            {
//                "All pilots are now on lap {lap}",
//            }
//            };




//            Event details
//            yield return new Callout()
//            {
//                Trigger = Trigger.EventName,
//                Alternatives = new string[]
//            {
//                "We're here at {event}",
//                "This is {event}"
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.RaceTypeLaps,
//                Alternatives = new string[]
//            {
//                "This is {race}",
//                "We're running a {timetrial} of {laps}"
//            }
//            };

//            yield return new Callout()
//            {
//                Trigger = Trigger.Gibberish,
//                Alternatives = new string[]
//            {
//                "How about this weather?",
//                "That local sports team sure is having a season. Go Team!",
//                "Did anyone else watch popular tv show last night?",
//                "Looks like those clowns in politics did it again. What a bunch of clowns."
//            }
//            };
//        }
//    }
//}
