using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public enum MultiGPRaceFormat
    {
        AggregateLaps = 0, //the format we have all been familiar with, most laps in X time.
        FastestLap = 1, //Sorts pilots by their fastest lap, requires timing system.
        Fastest3ConsecutiveLaps = 2, //Sorts pilots by their fastest 3 consecutive laps, requires timing system.
        BestRound = 3  //Sorts pilots by their best heat.
    }
}
