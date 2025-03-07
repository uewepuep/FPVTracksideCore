using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timing
{
    public class StartMetaData
    {
        public StartMetaData(Guid raceId, int raceNumber, int roundNumber, string raceName, string bracket)
        {
            this.RaceId = raceId;
            this.RaceNumber = raceNumber;
            this.RoundNumber = roundNumber;
            this.RaceName = raceName;
            this.Bracket = bracket;
        }

        public String RaceName { get; set; }
        public Guid RaceId { get; set; }
        public int RaceNumber { get; set; }
        public int RoundNumber { get; set; }
        public string Bracket { get; set; }
    }
}
