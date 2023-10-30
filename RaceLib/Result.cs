using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Result : BaseObjectT<DB.Result>
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

        public Result(DB.Result obj)
            : base(obj)
        {
            if (obj.Event != null)
                Event = obj.Event.Convert<Event>();
            if (obj.Pilot != null)
                Pilot = obj.Pilot.Convert<Pilot>();
            if (obj.Race != null)
                Race = obj.Race.Convert<Race>();
            if (obj.Round != null)
                Round = obj.Round.Convert<Round>();
        }

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
