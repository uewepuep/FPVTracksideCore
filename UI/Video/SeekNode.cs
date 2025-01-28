using Composition;
using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{
    public class SeekNode : Node
    {
        public event Action<DateTime> Seek;

        private ProgressBarNode progressBar;

        public DateTime CurrentTime
        {
            get
            {
                return FactorToTime(progressBar.Progress);
            }
            set
            {
                progressBar.Progress = TimeToFactor(value);
            }
        }

        public ImageButtonNode PlayButton { get; private set; }
        public TextCheckBoxNode SlowCheck { get; private set; }
        public ImageButtonNode StopButton { get; private set; }

        public TextButtonNode ShowAll { get; private set; }

        private Node buttonsNode;

        private Node flags;

        private EventManager eventManager;

        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public SeekNode(EventManager eventManager, Color color)
        {
            this.eventManager = eventManager;

            Node container = new Node();
            container.RelativeBounds = new RectangleF(0, 0.2f, 1, 0.5f);
            AddChild(container);

            buttonsNode = new Node();
            buttonsNode.RelativeBounds = new RectangleF(0, 0, 0.02f, 1);
            container.AddChild(buttonsNode);

            PlayButton = new ImageButtonNode(@"img\start.png", Color.Transparent, Theme.Current.Hover.XNA, color);
            buttonsNode.AddChild(PlayButton);

            StopButton = new ImageButtonNode(@"img\stop.png", Color.Transparent, Theme.Current.Hover.XNA, color);
            buttonsNode.AddChild(StopButton);

            float slowWidth = 0.05f;
            float showAllWidth = 0.05f;

            SlowCheck = new TextCheckBoxNode("Slow", color, false);
            SlowCheck.RelativeBounds = new RectangleF(1 - (showAllWidth + slowWidth), 0, slowWidth, 1);
            SlowCheck.SetRatio(0.6f, 0.05f);
            SlowCheck.Scale(1, 0.8f);
            container.AddChild(SlowCheck);

            ShowAll = new TextButtonNode("Show All", Color.Transparent, Theme.Current.Hover.XNA, color);
            ShowAll.RelativeBounds = new RectangleF(1 - showAllWidth, 0, showAllWidth, 1);
            ShowAll.Scale(1, 0.8f);
            ShowAll.TextNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            container.AddChild(ShowAll);

            progressBar = new ProgressBarNode(color);
            progressBar.RelativeBounds = new RectangleF(buttonsNode.RelativeBounds.Right, 0, 1 - (buttonsNode.RelativeBounds.Right + showAllWidth + slowWidth), 1);
            container.AddChild(progressBar);

            flags = new Node();
            flags.RelativeBounds = new RectangleF(progressBar.RelativeBounds.X, container.RelativeBounds.Bottom, progressBar.RelativeBounds.Width, 1 - container.RelativeBounds.Bottom);
            AddChild(flags);
        }

        public void ClearFlags()
        {
            flags.ClearDisposeChildren();
        }

        private DateTime FactorToTime(float factor)
        {
            TimeSpan length = End - Start;
            return Start + TimeSpan.FromSeconds(length.TotalSeconds * factor);
        }

        private float TimeToFactor(DateTime dateTime)
        {
            TimeSpan length = End - Start;
            TimeSpan time = dateTime - Start;

            return (float)(time.TotalSeconds / length.TotalSeconds);
        }

        public void SetRace(Race race, DateTime mediaStart, DateTime mediaEnd)
        {
            ClearFlags();

            Start = race.Start > mediaStart ? mediaStart : race.Start;
            End = race.End < mediaEnd ? mediaEnd : race.End;

            foreach (PilotChannel pilotChannel in race.PilotChannelsSafe)
            {
                Color tint = eventManager.GetChannelColor(pilotChannel.Channel);

                Lap[] laps = race.GetValidLaps(pilotChannel.Pilot, true);
                foreach (Lap l in laps)
                {
                    DateTime time = l.Detection.Time;
                    AddFlagAtTime(@"img/flag.png", time, tint, l.Number.ToString());
                }
            }

            

            AddFlagAtTime(@"img/raceflag.png", race.Start, Color.Green, "");
            AddFlagAtTime(@"img/raceflag.png", race.End, Color.White, "");

            if (race.Event.Flags != null)
            {
                IEnumerable<DateTime> flags = race.Event.Flags.Where(f => race.Start <= f && race.End >= f);
                foreach (DateTime flag in flags)
                {
                    AddFlagAtTime(@"img/raceflag.png", flag, Color.Beige, "F");
                }
            }

            RequestLayout();
        }

        private void AddFlagAtTime(string filename, DateTime time, Color tint, string text)
        {
            float factor = TimeToFactor(time);

            ImageNode flag = new ImageNode(filename, tint);

            float width = 0.02f;
            flag.RelativeBounds = new RectangleF(factor - width / 2, 0, width, 1f);
            flag.KeepAspectRatio = false;
            flag.CanScale = false;
            flag.Alignment = RectangleAlignment.Center;
            flags.AddChild(flag);

            TextNode textNode = new TextNode(text, tint);
            textNode.RelativeBounds = new RectangleF(0.035f, 0.75f, 1, 0.5f);
            textNode.Alignment = RectangleAlignment.BottomCenter;
            flag.AddChild(textNode);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (Mouse.GetState().LeftButton == ButtonState.Pressed && Seek != null)
            {
                if (progressBar.Contains(mouseInputEvent.Position))
                {
                    int x = mouseInputEvent.Position.X - progressBar.Bounds.X;
                    float factor = x / (float)progressBar.Bounds.Width;

                    DateTime time = FactorToTime(factor);
                    Seek(time);
                }
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }
}
