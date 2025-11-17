using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Format;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using static RaceLib.Round;

namespace UI.Nodes.Rounds
{
    public class EventXNode : RenderTargetNode
    {
        public TextButtonNode AddRoundButton { get; private set; }
        public TextButtonNode RemoveRoundButton { get; private set; }

        public EventManager EventManager { get; private set; }

        protected TextNode heading;
        protected TextNode subHeading;
        protected Node contentContainer;
        protected AspectNode buttonContainer;
        protected ColorNode headingbg;

        public delegate void RoundDelegate(Round round);
        public delegate void RoundStageDelegate(Round round, Stage stage);
        public delegate void RoundTimeDelegate(Round round, TimeSummary.TimeSummaryTypes type);

        public event RoundDelegate AddRound;
        public event RoundDelegate ChangeChannels;
        public event RoundDelegate Finals;
        public event RoundDelegate Clone;
        public event RoundDelegate AddSheetFormatRound;
        public event Action<Round, StageTypes, IEnumerable<Pilot>> AddStage;

        public event RoundDelegate RemoveRound;
        public event RoundDelegate SumPoints;
        public event RoundDelegate PackCount;
        public event RoundTimeDelegate Times;
        public event RoundDelegate LapCounts;

        public event RoundStageDelegate AddEmptyRound;
        public event RoundStageDelegate CustomRound;

        public event Action NeedsFormatLayout;

        public Round Round { get; private set; }

        protected BorderPanelNode panel;

        protected bool canSum;
        protected bool canAddTimes;
        protected bool canAddLapCount;
        protected bool canClone;
        protected bool canAddFinal;
        protected bool canAddRace;

        public virtual int Order
        {
            get
            {
                if (Round == null)
                    return 0;

                return Round.Order;
            }
        }

        public EventXNode(EventManager ev, Round round)
        {
            Scroller.Enabled = false;

            Round = round;

            EventManager = ev;

            panel = new BorderPanelNode(Theme.Current.Rounds.Background, Theme.Current.Rounds.Border.XNA);
            AddChild(panel);

            headingbg = new ColorNode(Theme.Current.Rounds.Heading);
            headingbg.RelativeBounds = new RectangleF(0, 0.0f, 1f, 0.05f);
            panel.AddChild(headingbg);

            heading = new TextNode("", Theme.Current.Rounds.Text.XNA);
            heading.Alignment = RectangleAlignment.CenterLeft;
            heading.RelativeBounds = new RectangleF(0.02f, 0.2f, 0.7f, 0.7f);
            headingbg.AddChild(heading);

            subHeading = new TextNode("", Theme.Current.Rounds.Text.XNA);
            subHeading.RelativeBounds = new RectangleF(0.0f, headingbg.RelativeBounds.Bottom, 1, 0.025f);
            subHeading.Alignment = RectangleAlignment.Center;
            panel.Inner.AddChild(subHeading);

            buttonContainer = new AspectNode(2.2f);
            buttonContainer.RelativeBounds = new RectangleF(heading.RelativeBounds.Width, 0, 1 - heading.RelativeBounds.Width, 1);
            buttonContainer.Alignment = RectangleAlignment.TopRight;
            headingbg.AddChild(buttonContainer);

            RemoveRoundButton = new TextButtonNode("-", Color.Transparent, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            RemoveRoundButton.OnClick += (mie) => { RemoveRound?.Invoke(Round); UpdateButtons(); };
            buttonContainer.AddChild(RemoveRoundButton);

            AddRoundButton = new TextButtonNode("+", Color.Transparent, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            AddRoundButton.OnClick += AddRoundButton_OnClick;
            buttonContainer.AddChild(AddRoundButton);

            contentContainer = new Node();
            contentContainer.RelativeBounds = new RectangleF(0, subHeading.RelativeBounds.Bottom, 1, 1 - subHeading.RelativeBounds.Bottom);
            panel.Inner.AddChild(contentContainer);
            UpdateButtons();
        }

        public virtual bool IsRoundInStage()
        {
            return false;
        }

        public virtual void SetHeading(string text)
        {
            heading.Text = text;
        }

        public void SetSubHeading(string text)
        {
            float lineHeight = 0.019f;
            float padding = 0.006f;

            int lines = 1 + text.Count(c => c == '\n');

            subHeading.RelativeBounds = new RectangleF(0.0f, headingbg.RelativeBounds.Bottom, 1, padding + lines * lineHeight);
            contentContainer.RelativeBounds = new RectangleF(0, subHeading.RelativeBounds.Bottom, 1, 1 - subHeading.RelativeBounds.Bottom);

            subHeading.Text = text;
        }

        private void AddRoundButton_OnClick(MouseInputEvent mie)
        {
            Point position = new Point(AddRoundButton.Bounds.X, AddRoundButton.Bounds.Bottom);
            MouseMenu main = new MouseMenu(this);

            AddButtonMenu(main);

            if (main.Items.Count() >= 1)
            {
                main.Show(position - mie.Translation);
            }
            else
            {
                main.Dispose();
            }
        }

        protected void RequestFormatLayout()
        {
            NeedsFormatLayout?.Invoke();
        }

        protected void AddButtonMenu(MouseMenu rootMenu)
        {
            if (canAddRace)
            {
                rootMenu.AddItem("Add Race", AddRace);
            }

            if (EventManager.ExternalRaceProviders != null)
            {
                if (EventManager.RoundManager.GetLastRound(Round.EventType, Round.Stage) == Round)
                {
                    if (EventManager.RaceManager.GetRaces(Round).All(r => r.Ended))
                    {
                        foreach (var external in EventManager.ExternalRaceProviders)
                        {
                            var t = external;
                            rootMenu.AddItem("Add " + external.Name, () => { t.TriggerCreateRaces(Round); });
                        }
                    }
                }
            }

            // Add round
            {
                MouseMenu addRound = rootMenu.AddSubmenu("Add Round");
                AddButtonRoundMenu(addRound);
            }

            if (!IsRoundInStage())
            {
                Pilot[] pilots = GetOrderedPilots().ToArray();
                if (EventManager.RoundManager.IsEmpty(Round))
                {
                    MouseMenu format = rootMenu.AddSubmenu("Set Format");
                    AddFormatMenu(format, pilots);
                }
                else
                {
                    MouseMenu format = rootMenu.AddSubmenu("Add Format");
                    AddFormatMenu(format, pilots);
                }
            }

            if (EventManager.ExternalRaceProviders != null)
            {
                if (EventManager.RoundManager.GetLastRound(Round.EventType, Round.Stage) == Round)
                {
                    foreach (var external in EventManager.ExternalRaceProviders)
                    {
                        var t = external;
                        rootMenu.AddItem("Add " + external.Name, () => { t.TriggerCreateRaces(Round); });
                    }
                }
            }

            if ((canSum || canAddTimes || canAddLapCount) && !IsRoundInStage())
            {
                MouseMenu results = rootMenu.AddSubmenu("Add Results Stage");
                if (canSum)
                    results.AddItem("Points Stage", () => { SumPoints?.Invoke(Round); });

                if (canAddTimes)
                {
                    results.AddItem("PB Records Stage", () => { Times?.Invoke(Round, TimeSummary.TimeSummaryTypes.PB); });
                    results.AddItem("Event Lap Records Stage", () => { Times?.Invoke(Round, TimeSummary.TimeSummaryTypes.EventLap); });
                    results.AddItem("Race Time Records Stage", () => { Times?.Invoke(Round, TimeSummary.TimeSummaryTypes.RaceTime); });
                }

                if (canAddLapCount)
                    results.AddItem("Lap Count Stage", () => { LapCounts?.Invoke(Round); });
                
                results.AddItem("Pack Count Stage", () => { PackCount?.Invoke(Round); });
            }
        }

        protected void AddFormatMenu(MouseMenu menu, IEnumerable<Pilot> orderedPilots)
        {
            MouseMenu sheets = menu.AddSubmenu("From Spreadsheet");
            if (EventManager.RoundManager.SheetFormatManager.Sheets.Any())
            {
                foreach (SheetFormatManager.SheetFile sheet in EventManager.RoundManager.SheetFormatManager.Sheets)
                {
                    string name = sheet.Name + " (" + sheet.Pilots + " pilots)";

                    var sheet2 = sheet;
                    sheets.AddItem(name, () => { SheetFormat(sheet2, orderedPilots); });
                }
            }

            menu.AddBlank();

            foreach (StageTypes stageType in Enum.GetValues<StageTypes>().Except([StageTypes.Default]))
            {
                string name = stageType.ToString().CamelCaseToHuman();
                StageTypes local = stageType;

                menu.AddItem(name, () => { AddStage?.Invoke(Round, local, orderedPilots); });
            }
        }

        protected virtual void AddButtonRoundMenu(MouseMenu addRound)
        {
            Stage stage = null;
            if (IsRoundInStage())
            {
                stage = Round.Stage;

                if (stage.GeneratesRounds)
                {
                    addRound.AddItem("Continue " + stage.Name, ContinueStageRound);
                    addRound.AddBlank();
                }
            }

            addRound.AddItem("Empty", () => { AddEmptyRound?.Invoke(Round, stage); });
            if (canClone)
            {
                addRound.AddItem("Clone", () => { Clone?.Invoke(Round); });
            }

            addRound.AddItem("Randomise (Random channels)", () => { ChangeChannels?.Invoke(Round); });
            addRound.AddItem("Randomise (Keep Channels)", () => { AddRound?.Invoke(Round); });

            if (canAddFinal)
                addRound.AddItem("Final", () => { Finals?.Invoke(Round); });
            addRound.AddItem("Custom Round", () => { CustomRound?.Invoke(Round, stage); });
        }

        private void ContinueStageRound()
        {
            if (Round.Stage == null)
                return; 

            if (Round.Stage.HasSheetFormat)
            {
                AddSheetFormatRound(Round);
            }
            else 
            {
                AddStage(Round, Round.Stage.StageType, GetOrderedPilots().ToArray());
            }
        }

        private void SheetFormat(SheetFormatManager.SheetFile sheet, IEnumerable<Pilot> orderedPilots)
        {
            LoadingLayer ll = GetLayer<LoadingLayer>();
            ll.WorkQueue.Enqueue("Loading format", () =>
            {
                Stage stage = null;
                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    stage = new Stage();
                    stage.ID = Guid.NewGuid();
                    stage.Name = sheet.Name;
                    stage.SheetFormatFilename = sheet.FileInfo.Name;

                    db.Insert(stage);

                    bool empty = !EventManager.RaceManager.GetRaces(Round).Any();
                    if (empty)
                    {
                        Round.Stage = stage;
                        db.Update(Round);
                    }
                }
                EventManager.RoundManager.SheetFormatManager.LoadSheet(stage, orderedPilots.ToArray(), true);
            });
        }

        protected void AddRace()
        {
            EventManager.RaceManager.AddRaceToRound(Round);
        }

        protected virtual void UpdateButtons()
        {
            AlignHorizontally(0.0f, buttonContainer.Children.ToArray());
        }

        public virtual void CalculateAspectRatio(float height)
        {
        }

        public override string ToString()
        {
            return heading.Text;
        }

        protected void SaveRound()
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                db.Update(Round);
            }
        }

        public virtual IEnumerable<Pilot> GetOrderedPilots()
        {
            return EventManager.Event.Pilots;
        }
    }
}
