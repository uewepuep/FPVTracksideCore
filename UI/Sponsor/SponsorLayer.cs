using Composition;
using Composition.Input;
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

        private TimeSpan fadeIn;

        public List<SponsorMedia> SponsorMedias { get; private set; }
        public SoundManager SoundManager { get; internal set; }
        public RaceManager RaceManager { get; internal set; }

        private DateTime lastInputTime;

        private ColorNode background;

        private Random random;

        private TextButtonNode endButton;
        public DateTime End { get; private set; }

        private SponsorNode sponsorNode;

        private Action afterTrigger;

        public bool ScreensaverMode { get; private set; }

        private Queue<SponsorMedia> screensaverQueue;

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
            lastInputTime = DateTime.Now;
        }

        public void Load()
        {
            Patreon[] patreons;

            using (IDatabase db = DatabaseFactory.Open(Guid.Empty))
            {
                patreons = db.All<Patreon>().Where(p => p.Active).ToArray();
            }

            SponsorMedias.Clear();

            SponsorMedias.Add(new SponsorMedia()
            {
                Filename = "img/logo.png",
                Text = "FPVTrackside is looking for sponsors. Join the patreon as at the sponsor level to have your message here. fpvtrackside.com"
            });
        }

        public void TriggerMaybe(Action afterTrigger)
        {
            if (DateTime.Now > triggered + TriggerPeriod)
            {
                Logger.UI.LogCall(this, "triggered after", DateTime.Now - triggered);
                this.afterTrigger = afterTrigger;
                Trigger();
            }
            else
            {
                Logger.UI.LogCall(this, "skipped, next in", triggered + TriggerPeriod - DateTime.Now);
                afterTrigger?.Invoke();
            }
        }

        private Queue<SponsorMedia> BuildShuffledQueue()
        {
            List<SponsorMedia> pool = new List<SponsorMedia>();
            foreach (SponsorMedia sponsor in SponsorMedias)
            {
                for (int i = 0; i < sponsor.Weight; i++)
                    pool.Add(sponsor);
            }

            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                SponsorMedia temp = pool[i];
                pool[i] = pool[j];
                pool[j] = temp;
            }

            return new Queue<SponsorMedia>(pool);
        }

        public void StartScreensaver()
        {
            Logger.UI.LogCall(this);
            afterTrigger = null;
            ScreensaverMode = true;
            screensaverQueue = BuildShuffledQueue();
            Trigger();
        }

        public void StopScreensaver()
        {
            if (!ScreensaverMode)
                return;
            Logger.UI.LogCall(this);
            ScreensaverMode = false;
            screensaverQueue = null;
            afterTrigger = null;
            lastInputTime = DateTime.Now;
            Close();
        }

        public void Trigger()
        {
            SponsorMedia chosen = null;

            if (ScreensaverMode && screensaverQueue != null)
            {
                if (screensaverQueue.Count == 0)
                {
                    Logger.UI.LogCall(this, "screensaver queue complete");
                    StopScreensaver();
                    return;
                }

                chosen = screensaverQueue.Dequeue();
            }
            else
            {
                int sumWeights = SponsorMedias.Select(s => s.Weight).Sum();

                if (sumWeights == 0)
                {
                    Logger.UI.LogCall(this, "no sponsors available");
                    afterTrigger?.Invoke();
                    return;
                }

                int result = random.Next(sumWeights);

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
                {
                    Logger.UI.LogCall(this, "no sponsor chosen");
                    afterTrigger?.Invoke();
                    return;
                }
            }

            Logger.UI.LogCall(this, chosen.Name, "screensaver", ScreensaverMode);

            triggered = DateTime.Now;
            Root.Alpha = 0.001f;
            Visible = true;

            sponsorNode?.Dispose();

            sponsorNode = new SponsorNode(SoundManager, chosen);
            Root.AddChild(sponsorNode);

            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(1, chosen.DurationSeconds));

            End = DateTime.Now + duration;

            RequestLayout();
        }

        private void OnClick(Composition.Input.MouseInputEvent mie)
        {
            if (ScreensaverMode)
                Trigger();
            else
                Close();
        }

        public void Close()
        {
            Logger.UI.LogCall(this);
            Visible = false;
            sponsorNode?.Dispose();

            SoundManager.Instance.StopSound();

            this.afterTrigger?.Invoke();
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            bool isOverTime = DateTime.Now - lastInputTime > TimeSpan.FromMinutes(ApplicationProfileSettings.Instance.ScreensaverIdleMinutes);

            bool noRaceRunning = RaceManager == null || (!RaceManager.RaceRunning && !RaceManager.PreRaceStartDelay);

            if (ScreensaverMode && !noRaceRunning)
            {
                StopScreensaver();
            }

            if (!ScreensaverMode &&
                noRaceRunning &&
                ApplicationProfileSettings.Instance.SponsoredByMessages &&
                SponsorMedias.Count > 0 &&
                ApplicationProfileSettings.Instance.ScreensaverIdleMinutes > 0 && isOverTime)
            {
                lastInputTime = DateTime.Now;
                StartScreensaver();
            }

            base.OnUpdate(gameTime);
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

            endButton.Text = ScreensaverMode ? "Next" : "Skip (" + remaining + ")";

            if (now > End)
            {
                if (ScreensaverMode)
                    Trigger();
                else
                    Close();
            }

            base.OnDraw();
        }

        public override bool OnMouseInput(MouseInputEvent inputEvent)
        {
            lastInputTime = DateTime.Now;

            if (ScreensaverMode && inputEvent.EventType == MouseInputEvent.EventTypes.Button)
            {
                StopScreensaver();
                return true;
            }

            return base.OnMouseInput(inputEvent);
        }
    }
}
