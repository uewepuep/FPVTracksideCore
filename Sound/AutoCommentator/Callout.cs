using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sound.AutoCommentator
{
    public enum Trigger
    {
        None,
        
        //Pilot stuff
        CrashesOut,
        GetsFirst,
        GainsPosition,
        CloseRace,
        FasterComparedToPrevious,
        JustBehind,
        LargeLead,
        Win,
        BehindLead,
        Consistent,

        //Race stuff
        AfterRaceLotsOfPositionChanges,
        RaceStart,
        AllOnLap,

        // Event details
        EventName,
        RaceTypeLaps,

        //Gibberish
        Gibberish
    }

    public class Callout
    {
    }

}
