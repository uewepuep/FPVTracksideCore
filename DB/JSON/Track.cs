using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class Track : DatabaseObjectT<RaceLib.Track>
    {
        public TrackElement[] TrackElements { get; set; }

        public string Name { get; set; }

        public float Length { get; set; }
        public int GridSize { get; set; }

        public Track() 
        { 
            GridSize = 5;
        }

        public Track(RaceLib.Track t)
            :base(t)
        {
            Copy(t.TrackElements, out TrackElement[] temp);
            TrackElements = temp;
        }

        public override RaceLib.Track GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Track t = base.GetRaceLibObject(database);

            Copy(TrackElements, out RaceLib.TrackElement[] temp);
            t.TrackElements = temp;

            return t;
        }
    }

    public class TrackElement
    {
        public RaceLib.TrackElement.ElementTypes ElementType { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public float Tilt { get; set; }
        public float Scale { get; set; }

        public float Rotation { get; set; }
        public bool Visible { get; set; }
        public bool Decorative { get; set; }

        public bool SplitEnd { get; set; }

        public TrackElement()
        {
            Scale = 1;
        }

    }
}
