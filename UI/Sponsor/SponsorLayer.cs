using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;

namespace UI.Sponsor
{
    public class SponsorLayer : CompositorLayer
    {
        private DateTime triggered;
        private int triggerCount;

        private TimeSpan fadeIn;

        public List<SponsorMedia> SponsorMedias { get; private set; }
        public SoundManager SoundManager { get; internal set; }

        private ColorNode background;

        private Random random;

        public SponsorLayer(GraphicsDevice device) 
            : base(device)
        {
            fadeIn = TimeSpan.FromSeconds(1);
            SponsorMedias = new List<SponsorMedia>();

            background = new ColorNode(Color.FromNonPremultiplied(4, 4, 4, 200));
            Root.AddChild(background);

            Load();

            Visible = false;
            random = new Random();
        }

        public void Load()
        {
            Patreon[] patreons;

            using (Database db = new Database())
            {
                patreons = db.Patreons.Find(p => p.Active).ToArray();
            }

            SponsorMedias.Clear();

            foreach (Patreon patreon in patreons)
            {
                int weight = 10;
                if (patreon.Tier != null)
                {
                    if (patreon.Tier.Contains("Pilot"))
                        weight = 2;
                    if (patreon.Tier.Contains("Club"))
                        weight = 12;
                    if (patreon.Tier.Contains("Developer"))
                        weight = 0;
                }

                SponsorMedias.Add(new SponsorMedia()
                {
                    Filename = patreon.ThumbFilename,
                    Name = patreon.Name,
                    Text = "The next race is brought to you by " + patreon.Name + "; supporting FPVTrackside since " + patreon.StartDate.ToString("MMMM") + " " + patreon.StartDate.Year,
                    DurationSeconds = 10,
                    Weight = weight,
                    AdType = AdType.Patreon,
                    Since = "Since " + patreon.StartDate.ToString("MMMM") + " " + patreon.StartDate.Year
                });
            }

            //SponsorMedias.Add(new SponsorMedia()
            //{
            //    Filename = "sponsor/media/tmotor.jpg",
            //    Text = "The next race is brought to you by; The T-Motor Flame 180AHV",
            //    DurationSeconds = 6
            //});
        }

        public void Trigger()
        {
            int sumWeights = SponsorMedias.Select(s => s.Weight).Sum();

            int result = random.Next(sumWeights);

            SponsorMedia chosen = null;

            int currentWeight = 0;
            foreach (SponsorMedia sponsor in SponsorMedias)
            {
                if (currentWeight < result && currentWeight + sponsor.Weight > result)
                {
                    chosen = sponsor;
                    break;
                }
                currentWeight += sponsor.Weight;
            }

            if (chosen == null)
                return;

            triggered = DateTime.Now;
            triggerCount++;
            Visible = true;
            Root.Alpha = 0;

            SponsorNode sponsorNode = new SponsorNode(SoundManager, chosen);
            Root.AddChild(sponsorNode);

            RequestLayout();
        }

        protected override void OnDraw()
        {
            if (Root.Alpha < 1)
            {
                float factor = (float)((DateTime.Now - triggered).TotalSeconds / fadeIn.TotalSeconds);
                Root.Alpha = factor;
                if (Root.Alpha > 1) Root.Alpha = 1;
            }

            base.OnDraw();
        }
    }
}
