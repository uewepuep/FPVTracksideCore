using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace DB
{
    public class Pilot : DatabaseObjectT<RaceLib.Pilot>
    {
        public string Name { get; set; }

        public string Phonetic { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string SillyName { get; set; }

        public string DiscordID { get; set; }

        public string Aircraft { get; set; }

        public string CatchPhrase { get; set; }

        public string BestResult { get; set; }

        public int TimingSensitivityPercent { get; set; }

        public bool PracticePilot { get; set; }

        public string PhotoPath { get; set; }
        public bool VideoFlipped { get; set; }
        public bool VideoMirrored { get; set; }

        public Pilot() { }

        public Pilot(RaceLib.Pilot obj)
            : base(obj)
        {
        }
    }


}
