using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Nodes
{
    public class SplitsNode : Node
    {
        private EventManager eventManager;
        private RaceManager raceManager;

        private List<SplitTimeNode> splitNodes;

        public Pilot Pilot { get; set; }
        public int Splits { get { return eventManager.RaceManager.TimingSystemManager.SplitsPerLap; } }

        public SplitsNode(EventManager eventManager)
        {
            this.eventManager = eventManager;
            raceManager = eventManager.RaceManager;

            splitNodes = new List<SplitTimeNode>();

            raceManager.OnSplitDetection += OnSplitDetection;
            raceManager.OnLapDetected += OnSplitDetection;
        }

        public override void Dispose()
        {
            raceManager.OnSplitDetection -= OnSplitDetection;
            raceManager.OnLapDetected -= OnSplitDetection;

            base.Dispose();
        }

        public void SetPilot(Pilot pilot)
        {
            if (Pilot != pilot)
            {
                Pilot = pilot;
            }
            Init();
        }

        private void OnSplitDetection(Lap lap)
        {
            OnSplitDetection(lap.Detection);
        }

        private void OnSplitDetection(Detection detection)
        {
            Race race = raceManager.CurrentRace;
            if (race == null)
                return;

            if (Pilot != detection.Pilot)
                return;

            if (detection.IsHoleshot)
                return;

            Split split = race.GetSplit(detection);
            if (split != null)
            {
                int index = detection.TimingSystemIndex;

                SplitTimeNode splitNode = null;
                if (index < splitNodes.Count && index > 0)
                {
                    splitNode = splitNodes[index - 1];
                }
                else if (splitNodes.Any())
                {
                    splitNode = splitNodes.Last();
                }

                if (splitNode != null)
                {
                    splitNode.SetTime(split);

                    if (detection.IsLapEnd)
                    {
                        foreach (SplitTimeNode sn in splitNodes)
                        {
                            sn.ToNormal();
                        }
                    }
                }
            }
        }

        public void Clear()
        {
            Init();
        }

        private void Init()
        {
            splitNodes.Clear();

            if (Splits == 1)
            {
                this.Visible = false;
                return;
            }
            this.Visible = true;

            ClearDisposeChildren();

            for (int i = 0; i < Splits; i++)
            {
                SplitTimeNode tn = new SplitTimeNode(i + 1);
                AddChild(tn);
                splitNodes.Add(tn);
            }

            AlignHorizontally(0.05f, splitNodes.ToArray());
        }

        private class SplitTimeNode : ChangeAlphaTextNode
        {
            public TimeSpan Time { get; set; }
            public Pilot Pilot { get; set; }
            public int SplitNumber { get; set; }


            public SplitTimeNode(int splitNumber)
                : base("", Theme.Current.TextMain.XNA)
            {
                SplitNumber = splitNumber;
                Style.Border = true;
                Time = TimeSpan.MaxValue;
                Pilot = null;
            }

            public void SetTime(Split split)
            {
                if (split == null)
                    return;

                string text = "S" + SplitNumber + " " + split.Time.ToStringRaceTime();

                if (Pilot == split.Detection.Pilot)
                {
                    TimeSpan diff = split.Time - Time;

                    if (diff.TotalSeconds > 0)
                    {
                        Style.TextColor = Theme.Current.BehindTime.XNA;
                    }
                    else
                    {
                        Style.TextColor = Theme.Current.AheadTime.XNA;
                    }
                }
                else
                {
                    Style.TextColor = Theme.Current.TextMain.XNA;
                }

                Text = text;
                ToHighLight();
                Snap();

                Time = split.Time;
                Pilot = split.Detection.Pilot;
            }

            public void Clear()
            {
                Time = TimeSpan.MaxValue;
                Pilot = null;
                Text = "";
            }
        }
    }
}
