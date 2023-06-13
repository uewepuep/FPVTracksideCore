using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Sponsor
{
    public enum AdType
    {
        None,
        Video,
        Image,
        Patreon
    }

    public class SponsorMedia
    {
        public string Filename { get; set; }
        public string Text { get; set; }
        public string Name { get; set; }
        public string Since { get; set; }
        public float DurationSeconds { get; set; }

        public AdType AdType { get; set; }
        public int Weight { get; set; }

        public SponsorMedia()
        {
            Filename = "";
            Text = "";
            DurationSeconds = 5;
            Weight = 1;
            Since = "";
            Name = "";
        }
    }
}
