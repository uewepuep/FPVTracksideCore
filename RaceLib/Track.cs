using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Track : BaseObject, ICloneable
    {
        public TrackElement[] TrackElements { get; set; }

        public string Name { get; set; }

        public float Length { get; set; }

        public Track() 
        { 
        }

        public object Clone()
        {
            return new Track()
            {
                Name = Name,
                Length = Length,
                TrackElements = TrackElements.Select(x => (TrackElement)x.Clone()).ToArray()
            };
        }
    }

    public class TrackElement: ICloneable
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

        public TrackElement()
        {
        }

        public override string ToString()
        {
            return ElementType.ToString();
        }

        public object Clone()
        {
            return new TrackElement()
            {
                ElementType = ElementType,
                Position = Position,
                TimingSystemIndex = TimingSystemIndex,
                Tilt = Tilt,
                Rotation = Rotation,
                Visible = Visible
            };
        }
    }
}
