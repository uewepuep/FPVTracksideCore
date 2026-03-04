using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Sponsor
{
    public class SponsorMedia
    {
        public string Filename { get; set; }
        public string Text { get; set; }
        public string Name { get; set; }
        public string Since { get; set; }
        public float DurationSeconds { get; set; }

        public RaceLib.AdType AdType { get; set; }
        public int Weight { get; set; }

        public SponsorMedia()
        {
            Filename = "";
            Text = "";
            DurationSeconds = 10;
            Weight = 1;
            Since = "";
            Name = "";
        }
    }
}
