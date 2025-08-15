using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace DB
{
    public class Round : DatabaseObjectT<RaceLib.Round>
    {
        public string Name { get; set; }

        public int RoundNumber { get; set; }

        public string EventType { get; set; }

        public string RoundType { get; set; }

        public bool Valid { get; set; }

        public int Order { get; set; }

        public DateTime ScheduledStart { get; set; }

        public string GameTypeName { get; set; }

        public Round() { }

        public Round(RaceLib.Round obj)
            : base(obj)
        {
            
        }
    }
}
