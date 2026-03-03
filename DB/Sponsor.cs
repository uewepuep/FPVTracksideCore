using System;
using RaceLib;

namespace DB
{
    public class Sponsor : DatabaseObjectT<RaceLib.Sponsor>
    {
        public string Name { get; set; }
        public string Filename { get; set; }
        public string Text { get; set; }
        public float DurationSeconds { get; set; }
        public AdType AdType { get; set; }
        public int Weight { get; set; }
        public string Since { get; set; }
        public bool Active { get; set; }

        public Sponsor()
        {
        }

        public Sponsor(RaceLib.Sponsor obj)
            : base(obj)
        {
        }
    }
}
