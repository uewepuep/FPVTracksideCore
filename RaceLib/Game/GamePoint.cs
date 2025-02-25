using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;

namespace RaceLib.Game
{
    public class GamePoint : BaseObject
    {
        public Channel Channel { get; set; }
        public DateTime Time { get; set; }

        public Pilot Pilot { get; set; }
        public bool Valid { get; set; }

        public GamePoint()
        {
            Valid = true;
            Time = DateTime.Now;
        }
    }
}
