using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public abstract class PBNode : Node
    {
        public EventManager EventManager { get; private set; }

        private Pilot pilot;

        public Pilot Pilot
        {
            get
            {
                return pilot;
            }
            set
            {
                pilot = value;
                Refresh();
                flashy.Stop();
            }
        }

        public abstract bool HasPB { get; }

        protected TextNode textNode;
        protected AlphaFlashyNode flashy;

        protected Race race;

        public DateTime DetectionTime { get; protected set; }

        public PBNode(EventManager eventManager, Color textColor)
        {
            EventManager = eventManager;

            flashy = new AlphaFlashyNode();
            AddChild(flashy);

            textNode = new TextNode("", textColor);
            textNode.Alignment = RectangleAlignment.CenterLeft;

            flashy.AddChild(textNode);

            EventManager.RaceManager.OnLapsRecalculated += Refresh;
            EventManager.RaceManager.OnRaceReset += Refresh;
        }


        public override void Dispose()
        {
            EventManager.RaceManager.OnLapsRecalculated -= Refresh;
            EventManager.RaceManager.OnRaceReset -= Refresh;
            base.Dispose();
        }


        private void Refresh(Race r)
        {
            Refresh();
        }

        protected virtual void Refresh()
        {
            
        }

        public abstract void MouseMenu(MouseMenu mm);
    }

    public class PBSpeedNode : PBNode
    {
        public Split Split { get; private set; }
        public float Speed { get; private set; }
        public override bool HasPB { get { return Speed != 0; } }

        public PBSpeedNode(EventManager eventManager, Color textColor)
            :base(eventManager, textColor)
        {
            Speed = 0;
            
            EventManager.SpeedRecordManager.OnNewOveralBest += SpeedRecordManager_OnNewOveralBest;
            EventManager.SpeedRecordManager.OnNewPersonalBest += SpeedRecordManager_OnNewPersonalBest;
        }

        private void SpeedRecordManager_OnNewPersonalBest(Split split, float speed)
        {
            if (Pilot == split.Pilot)
            {
                SetPersonalBest(split, speed, false);
            }
        }

        private void SpeedRecordManager_OnNewOveralBest(Split split, float speed)
        {
            SetTint(Pilot == split.Pilot);
        }

        public override void Dispose()
        {
            EventManager.SpeedRecordManager.OnNewOveralBest -= SpeedRecordManager_OnNewOveralBest;
            EventManager.SpeedRecordManager.OnNewPersonalBest -= SpeedRecordManager_OnNewPersonalBest;

            base.Dispose();
        }
        protected override void Refresh()
        {
            if (Pilot != null)
            {
                Split split;
                float speed;
                bool overalBest;
                if (EventManager.SpeedRecordManager.GetBestSpeed(Pilot, out split, out speed, out overalBest))
                {
                    SetPersonalBest(split, speed, overalBest);
                    return;
                }
            }
            Speed = 0;
            textNode.Text = "";
            textNode.Tint = Color.White;
        }

        private void SetPersonalBest(Split split, float speed, bool isBest)
        {
            if (speed != Speed)
            {
                Split = split;

                if (speed > Speed)
                {
                    flashy.Flash();
                }

                Speed = speed;


                textNode.Text = "PB " + EventManager.SpeedRecordManager.SpeedToString(speed, ApplicationProfileSettings.Instance.Units);
            }
            SetTint(isBest);
            race = Split.Race;
            DetectionTime = split.Detection.Time;
        }

        protected void SetTint(bool isBest)
        {
            if (isBest)
            {
                textNode.Tint = Theme.Current.OverallBestTime.XNA;
            }
            else
            {
                Race current = EventManager.RaceManager.CurrentRace;
                if (current == race)
                {
                    textNode.Tint = Theme.Current.NewPersonalBest.XNA;
                }
                else
                {
                    textNode.Tint = Color.White;
                }
            }
        }

        public override void MouseMenu(MouseMenu mm)
        {
            if (HasPB && Split != null)
                mm.AddItem("Disqualify Sector / Speed", () =>
                {
                    EventManager.RaceManager.DisqualifySplit(Split);
                });
        }
    }

    public class PBTimeNode : PBNode
    {
        public Lap[] Laps { get; private set; }
        public int LapCount { get; set; }

        public TimeSpan Time { get; private set; }
        public override bool HasPB { get { return Time != TimeSpan.MaxValue; } }

        public event System.Action OnNewPB;

        public PBTimeNode(EventManager eventManager, Color textColor)
            : base(eventManager, textColor)
        {
            Time = TimeSpan.MaxValue;

            LapCount = EventManager.Event.PBLaps;

            EventManager.LapRecordManager.OnNewPersonalBest += RecordManager_OnNewPersonalBest;
            EventManager.LapRecordManager.OnNewOveralBest += RecordManager_OnNewOveralBest;
            EventManager.OnEventChange += EventManager_OnEventChange;
        }


        public override void Dispose()
        {
            EventManager.LapRecordManager.OnNewPersonalBest -= RecordManager_OnNewPersonalBest;
            EventManager.LapRecordManager.OnNewOveralBest -= RecordManager_OnNewOveralBest;
            EventManager.OnEventChange -= EventManager_OnEventChange;

            base.Dispose();
        }

        private void RecordManager_OnNewPersonalBest(Pilot p, int lc, Lap[] l)
        {
            if (Pilot == p)
                SetPersonalBest(lc, l, false);
        }

        private void RecordManager_OnNewOveralBest(Pilot p, int lc, Lap[] l)
        {
            SetTint(lc, l, Pilot == p);
        }

        private void EventManager_OnEventChange()
        {
            LapCount = EventManager.Event.PBLaps;
            Refresh();
        }

        protected override void Refresh()
        {
            if (Pilot != null)
            {
                Lap[] laps;
                bool overalBest;
                if (EventManager.LapRecordManager.GetBestLaps(Pilot, LapCount, out laps, out overalBest))
                {
                    SetPersonalBest(LapCount, laps, overalBest);
                    return;
                }
            }
            Time = TimeSpan.MaxValue;
            textNode.Text = "";
            textNode.Tint = Color.White;
        }

        private void SetPersonalBest(int recordLapCount, Lap[] laps, bool isBest)
        {
            if (recordLapCount == LapCount && laps.Length > 0)
            {
                Laps = laps;

                TimeSpan newTime = laps.TotalTime();
                if (newTime.TotalSeconds != Time.TotalSeconds)
                {
                    if (Laps.Length != 0 && laps.FirstOrDefault() != Laps.FirstOrDefault() && newTime < Time)
                    {
                        flashy.Flash();
                    }

                    Time = laps.TotalTime();

                    textNode.Text = "PB " + Time.ToStringRaceTime();
                    OnNewPB?.Invoke();
                }
                SetTint(recordLapCount, laps, isBest);
                DetectionTime = Laps.LastOrDefault().Detection.Time;
            }
            else
            {
                textNode.Tint = Color.White;
            }
        }

        private void SetTint(int recordLapCount, Lap[] laps, bool isBest)
        {
            if (recordLapCount == LapCount && laps.Any())
            {
                if (isBest)
                {
                    textNode.Tint = Theme.Current.OverallBestTime.XNA;
                }
                else
                {
                    Race current = EventManager.RaceManager.CurrentRace;
                    if (current == laps.First().Race)
                    {
                        textNode.Tint = Theme.Current.NewPersonalBest.XNA;
                    }
                    else
                    {
                        textNode.Tint = Color.White;
                    }
                }
            }
        }

        public override void MouseMenu(MouseMenu mm)
        {
            if (HasPB)
                LapRecordsSummaryNode.AddContextMenus(mm, EventManager, Laps);
        }

    }
}