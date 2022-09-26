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
    }

    public class SponsorMedia
    {
        public string Filename { get; set; }
        public string Text { get; set; }
        public float DurationSeconds { get; set; }

        public AdType AdType
        {
            get
            {
                string lower = Filename.ToLower();

                if (lower.Contains(".wmv")) return AdType.Video;
                if (lower.Contains(".mp4")) return AdType.Video;


                if (lower.Contains(".png")) return AdType.Image;
                if (lower.Contains(".jpg")) return AdType.Image;

                return AdType.None;
            }
        }

        public SponsorMedia()
        {
            Filename = "";
            Text = "";
            DurationSeconds = 5;
        }
    }
}
