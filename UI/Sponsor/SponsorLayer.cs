using Composition;
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

        private TextButtonNode endButton;
        public DateTime End { get; private set; }

        private SponsorNode sponsorNode;

        private Action afterTrigger;

        public TimeSpan TriggerPeriod { get; private set; }

        public SponsorLayer(GraphicsDevice device) 
            : base(device)
        {
            endButton = new TextButtonNode("", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            endButton.RelativeBounds = new RectangleF(0.89f, 0.94f, 0.1f, 0.05f);
            endButton.OnClick += OnClick;

            TriggerPeriod = TimeSpan.FromHours(1);

            fadeIn = TimeSpan.FromSeconds(1);
            SponsorMedias = new List<SponsorMedia>();

            background = new ColorNode(Color.FromNonPremultiplied(4, 4, 4, 200));
            Root.AddChild(background);
            Root.AddChild(endButton);

            Load();

            Visible = false;
            random = new Random(DateTime.Now.Millisecond);
            triggered = DateTime.Now;
        }

        public void Load()
        {
            Patreon[] patreons;

            using (Database db = new Database())
            {
                patreons = db.All<Patreon>().Where(p => p.Active).ToArray();
            }

            SponsorMedias.Clear();

            foreach (Patreon patreon in patreons)
            {
                if (patreon.Active && patreon.Amount > 0)
                {
                    SponsorMedias.Add(new SponsorMedia()
                    {
                        Filename = patreon.ThumbFilename,
                        Name = patreon.Name,
                        Text = "The next race is brought to you by " + patreon.Name + "; supporting FPVTrackside on Patreon since " + patreon.StartDate.ToString("MMMM") + " " + patreon.StartDate.Year,
                        DurationSeconds = 10,
                        Weight = patreon.Amount,
                        AdType = AdType.Patreon,
                        Since = "Since " + patreon.StartDate.ToString("MMMM") + " " + patreon.StartDate.Year
                    });
                }
            }

            //SponsorMedias.Add(new SponsorMedia()
            //{
            //    Filename = "sponsor/media/tmotor.jpg",
            //    Text = "The next race is brought to you by; The T-Motor Flame 180AHV",
            //    DurationSeconds = 6
            //});
        }

        public void TriggerMaybe(Action afterTrigger)
        {
            if (DateTime.Now > triggered + TriggerPeriod)
            {
                this.afterTrigger = afterTrigger;
                Trigger();
            }
            else
            {
                afterTrigger?.Invoke();
            }
        }

        public void Trigger()
        {
            int sumWeights = SponsorMedias.Select(s => s.Weight).Sum();

            int result = random.Next(sumWeights);

            SponsorMedia chosen = null;

            int currentWeight = 0;
            foreach (SponsorMedia sponsor in SponsorMedias)
            {
                if (currentWeight <= result && currentWeight + sponsor.Weight > result)
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
            Root.Alpha = 0.001f;
            Visible = true;

            sponsorNode?.Dispose();

            sponsorNode = new SponsorNode(SoundManager, chosen);
            Root.AddChild(sponsorNode);

            TimeSpan duration = TimeSpan.FromSeconds(chosen.DurationSeconds);

            End = DateTime.Now + duration;

            RequestLayout();
        }

        private void OnClick(Composition.Input.MouseInputEvent mie)
        {
            Close();
        }

        public void Close()
        {
            Visible = false;
            sponsorNode?.Dispose();

            SoundManager.Instance.StopSound();

            this.afterTrigger?.Invoke();
        }

        protected override void OnDraw()
        {
            if (Root.Alpha < 1)
            {
                float factor = (float)((DateTime.Now - triggered).TotalSeconds / fadeIn.TotalSeconds);
                Root.Alpha = factor;
                if (Root.Alpha > 1) Root.Alpha = 1;
            }

            DateTime now = DateTime.Now;

            int remaining = (int)Math.Ceiling((End - now).TotalSeconds);

            endButton.Text = "Skip (" + remaining + ")";

            if (now > End)
            {
                Close();
            }

            base.OnDraw();
        }
    }
}
