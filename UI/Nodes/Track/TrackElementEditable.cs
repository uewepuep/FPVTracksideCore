using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using ThreeDee.Entities;

namespace UI.Nodes.Track
{
    public class TrackElementEditable
    {

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public float Rotation { get; set; }

        public TrackElementEditable(TrackElement trackElement) 
        { 
            X = trackElement.Position.X;
            Y = trackElement.Position.Y;
            Z = trackElement.Position.Z;
        }
    }
}
