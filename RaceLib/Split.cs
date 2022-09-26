using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Split
    {
        public Race Race { get; private set; }
        public TimeSpan Time { get; private set; }
        public Detection Detection { get; private set; }

        public Pilot Pilot { get { return Detection.Pilot; } }

        public Split(Race race, Detection detection, TimeSpan time)
        {
            Race = race;
            Time = time;
            Detection = detection;
        }
    }
}
