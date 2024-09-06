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

        public int GridSize { get; set; }

        public Track() 
        {
            TrackElements = new TrackElement[0];
            GridSize = 5;
        }

        public object Clone()
        {
            return new Track()
            {
                Name = Name,
                Length = Length,
                TrackElements = TrackElements.Select(x => (TrackElement)x.Clone()).ToArray(),
                GridSize = GridSize
            };
        }

        public override bool Equals(object obj)
        {
            Track other = obj as Track;
            if (other == null) 
                return false;

            return ID.Equals(other.ID);
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
        public bool SplitEnd { get; set; }

        public float Tilt { get; set; }

        public float Rotation { get; set; }
        public bool Visible { get; set; }
        public bool Decorative { get; set; }

        // So we don't have to reference vector3 in db.
        public float X { get { return Position.X; } set { Position.X = value; } }
        public float Y { get { return Position.Y; } set { Position.Y = value; } }
        public float Z { get { return Position.Z; } set { Position.Z = value; } }

        public float Scale { get; set; }
        public TrackElement()
        {
            Scale = 1.0f;
            Visible = true;
            ElementType = ElementTypes.Gate;
        }

        public IEnumerable<Vector3> GetFlightPath()
        {
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.ToRadians(Rotation));

            foreach (Vector3 v in GetLocalFlightPath())
            {
                yield return Position + Vector3.Transform(v, rotation);
            }
        }

        public IEnumerable<Vector3> GetLocalFlightPath()
        {
            switch (ElementType) 
            {
                case ElementTypes.Gate:
                    yield return new Vector3(0, 1, 0);
                    break;

                case ElementTypes.Flag:
                    yield return new Vector3(0, 1, 1);
                    break;

                case ElementTypes.Dive:
                    yield return new Vector3(0, 3, 0);
                    yield return new Vector3(0, 1, 1);
                    break;

                case ElementTypes.Up:
                    yield return new Vector3(0, 1, 1);
                    yield return new Vector3(0, 3, 0);
                    break;
            }
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
                SplitEnd = SplitEnd,
                Tilt = Tilt,
                Rotation = Rotation,
                Visible = Visible,
                Decorative = Decorative,
                Scale = Scale,
            };
        }
    }
}
