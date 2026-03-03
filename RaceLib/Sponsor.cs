using System;

namespace RaceLib
{
    public enum AdType
    {
        None,
        Video,
        Image,
        Patreon
    }

    public class Sponsor : BaseObject
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
            DurationSeconds = 10;
            Weight = 1;
            AdType = AdType.Image;
        }
    }
}
