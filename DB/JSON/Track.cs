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

        public Track() { }

        public Track(RaceLib.Track t)
            :base(t)
        { 
            TrackElements = new TrackElement[t.TrackElements.Length];
            for(int i = 0; i < t.TrackElements.Length; i++)
            {
                TrackElements[i] = new TrackElement();
                Copy(t.TrackElements[i], TrackElements[i]);
            }
        }

        public override RaceLib.Track GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Track t = base.GetRaceLibObject(database);
            t.TrackElements = new RaceLib.TrackElement[TrackElements.Length];

            for (int i = 0; i < TrackElements.Length; i++)
            {
                t.TrackElements[i] = new RaceLib.TrackElement();
                Copy(TrackElements[i], t.TrackElements[i]);
            }

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

        public float Rotation { get; set; }
        public bool Visible { get; set; }
        public bool Decorative { get; set; }

        public bool SplitEnd { get; set; }

    }
}
