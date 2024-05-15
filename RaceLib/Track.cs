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

        public Vector3 Position;

        public float X
        {
            get
            {
                return Position.X;
            }
            set
            {
                Position.X = value;
            }
        }

        public float Y
        {
            get
            {
                return Position.Y;
            }
            set
            {
                Position.Y = value;
            }
        }
        public float Z
        {
            get
            {
                return Position.Z;
            }
            set
            {
                Position.Z = value;
            }
        }

        public int? TimingSystemIndex { get; set; }

        public float Tilt { get; set; }

        public float Rotation { get; set; }
        public bool Visible { get; set; }

        public override string ToString()
        {
            return ElementType.ToString();
        }
    }
}
