using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sound;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Sponsor
{
    public class SponsorLayer : CompositorLayer
    {
        private DateTime triggered;
        private int triggerCount;

        private TimeSpan fadeIn;

        public SponsorMedia[] SponsorMedias { get; private set; }
        public SoundManager SoundManager { get; internal set; }

        private ColorNode background;

        public SponsorLayer(GraphicsDevice device) 
            : base(device)
        {
            fadeIn = TimeSpan.FromSeconds(1);

            background = new ColorNode(Color.FromNonPremultiplied(4, 4, 4, 200));
            Root.AddChild(background);

            Load();

            Visible = false;
        }

        public void Load()
        {
            SponsorMedias = new SponsorMedia[] 
            {
                new SponsorMedia()
                {
                    Filename = "sponsor/media/tmotor.jpg",
                    Text = "The next race is brought to you by; The T-Motor Flame 180AHV",
                    DurationSeconds = 6
                }
            };
        }

        public void Trigger()
        {
            triggered = DateTime.Now;
            triggerCount++;
            Visible = true;
            Root.Alpha = 0;

            SponsorMedia media = SponsorMedias.Random();

            SponsorNode sponsorNode = new SponsorNode(SoundManager, media);
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
