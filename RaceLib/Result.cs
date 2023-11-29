using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Result : BaseObject
    {
        public int Points { get; set; }
        public int Position { get; set; }

        public bool Valid { get; set; }

        [System.ComponentModel.Browsable(false)]
        public Event Event { get; set; }

        [System.ComponentModel.Browsable(false)]
        public Pilot Pilot { get; set; }

        [System.ComponentModel.Browsable(false)]
        public Race Race { get; set; }

        [System.ComponentModel.Browsable(false)]
        public Round Round { get; set; }

        public bool DNF { get; set; }

        public enum ResultTypes
        {
            Race,
            RoundRollOver
        }

        public ResultTypes ResultType { get; set; }

        public Result()
        {
            Valid = true;
        }

        public override string ToString()
        {
            if (DNF)
                return Pilot.Name + " DNF";

            return Pilot.Name + " " + Points + " " + Position.ToStringPosition();
        }
    }
}
