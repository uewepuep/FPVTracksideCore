using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Sector
    {
        [DisplayName("TrackElementStart")]
        public int TrackElementStartIndex { get; set; }

        [DisplayName("TrackElementEnd")]
        public int TrackElementEndIndex { get; set; }

        public float Length { get; set; }

        [Browsable(false)]
        public Color Color { get; set; }

        [Browsable(false)]
        public int Number { get; set; }

        public bool CalculateSpeed { get; set; }

        public Sector()
        {
            CalculateSpeed = true;
        }

        public string ToString(Units units)
        {
            return "S" + Number + " " + GetLengthHuman(units);
        }

        public string GetLengthHuman(Units units)
        {
            return LengthHuman(units, Length);
        }

        public void SetLengthHuman(Units units, float value)
        {
            if (units == Units.Imperial)
            {
                Length = value / 3.28f;
            }
            else
            {
                Length = value;
            }
        }

        public static string LengthHuman(Units units, float length)
        {
            if (units == RaceLib.Units.Imperial)
            {
                length = length * 3.28f;

                return length.ToString("0.0") + "ft";
            }
            else
            {
                return length.ToString("0.0") + "m";
            }
        }


        public override string ToString()
        {
            return "Sector " + Number;
        }
    }
}
