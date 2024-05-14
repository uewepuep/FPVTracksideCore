using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Track : BaseObject
    {
        public TrackElement[] TrackElements { get; set; }

        public string Name { get; set; }

        public float Length { get; set; }

        public Track() 
        { 
        }
    }

    public class TrackElement
    {
        public enum ElementTypes
        {
            Invalid,
            Gate,
            Flag,
            Dive,
            Up
        }

        public ElementTypes ElementType { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public int? TimingSystemIndex { get; set; }

        public float Tilt { get; set; }

        public float Rotation { get; set; }
    }
}
