using Composition;
using Composition.Input;
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
        public delegate void RoundTimeDelegate(Round round, TimeSummary.TimeSummaryTypes type);

        public event RoundDelegate AddRound;
        public event RoundDelegate ChangeChannels;
        public event RoundDelegate Finals;
        public event RoundDelegate Clone;
        public event RoundDelegate AddEmptyRound;
        public event RoundDelegate DoubleElim;

        public event RoundDelegate RemoveRound;
        public event RoundDelegate SumPoints;
        public event RoundTimeDelegate Times;
        public event RoundDelegate LapCounts;

        public event RoundDelegate CustomRound;

        public event Action NeedsFormatLayout;

        public Round Round { get; private set; }

        protected BorderPanelNode panel;

        protected bool canSum;
        protected bool canAddTimes;
        protected bool canAddLapCount;
        protected bool canClone;
        protected bool canAddFinal;
        protected bool canAddRace;

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

        public void SetHeading(string text)
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

            var sf = EventManager.RoundManager.SheetFormatManager.GetRoundSheetFormat(Round);
            if (sf != null)
            {
                rootMenu.AddItem("Add Next Sheet Round", AddSheetFormatRound);
            }

            // Add round
            {
                MouseMenu addRound = rootMenu.AddSubmenu("Add Round");
                AddButtonRoundMenu(addRound);
            }

            if (GetOrderedPilots().Any())
            {
                AddFormatMenu(rootMenu, "Add Format");
            }
            else
            {
                AddFormatMenu(rootMenu, "Set Format");
            }

            if (canSum || canAddTimes || canAddLapCount)
            {
                MouseMenu results = rootMenu.AddSubmenu("Show Results");
                if (canSum)
                    results.AddItem("Show Points", () => { SumPoints?.Invoke(Round); });

                if (canAddTimes)
                {
                    results.AddItem("Show PB Records", () => { Times?.Invoke(Round, TimeSummary.TimeSummaryTypes.PB); });
                    results.AddItem("Show Event Lap Records", () => { Times?.Invoke(Round, TimeSummary.TimeSummaryTypes.EventLap); });
                    results.AddItem("Show Race Time Records", () => { Times?.Invoke(Round, TimeSummary.TimeSummaryTypes.RaceTime); });
                }

                if (canAddLapCount)
                    results.AddItem("Show Lap Count", () => { LapCounts?.Invoke(Round); });
            }
        }

        protected void AddFormatMenu(MouseMenu rootMenu, string menuname)
        {
            MouseMenu addFormat = rootMenu.AddSubmenu(menuname);
            addFormat.AddItem("Double Elimination", () => { DoubleElim?.Invoke(Round); });


            //add format
            if (EventManager.RoundManager.SheetFormatManager.Sheets.Any())
            {
                foreach (SheetFormatManager.SheetFile sheet in EventManager.RoundManager.SheetFormatManager.Sheets)
                {
                    string name = sheet.Name + " (" + sheet.Pilots + " pilots)";

                    var sheet2 = sheet;
                    addFormat.AddItem(name, () => { SheetFormat(sheet2); });
                }
            }
        }

        protected virtual void AddButtonRoundMenu(MouseMenu addRound)
        {
            addRound.AddItem("Empty", () => { AddEmptyRound?.Invoke(Round); });
            if (canClone)
            {
                addRound.AddItem("Clone", () => { Clone?.Invoke(Round); });
            }

            addRound.AddItem("Randomise (Random channels)", () => { ChangeChannels?.Invoke(Round); });
            addRound.AddItem("Randomise (Keep Channels)", () => { AddRound?.Invoke(Round); });

            if (canAddFinal)
                addRound.AddItem("Final", () => { Finals?.Invoke(Round); });
            addRound.AddItem("Custom Round", () => { CustomRound?.Invoke(Round); });
        }

        private void AddSheetFormatRound()
        {
            RoundSheetFormat roundSheetFormat = EventManager.RoundManager.SheetFormatManager.GetRoundSheetFormat(Round);

            if (EventManager.RaceManager.GetRaces(Round).Any() && roundSheetFormat != null)
            {
                Round newRound = EventManager.RoundManager.GetCreateRound(Round.RoundNumber + 1, Round.EventType);
                roundSheetFormat.GenerateSingleRound(newRound);
            }
        }

        private void SheetFormat(SheetFormatManager.SheetFile sheet)
        {
            Round newRound;

            if (EventManager.RaceManager.GetRaces(Round).Any())
            {
                newRound = EventManager.RoundManager.GetCreateRound(Round.RoundNumber + 1, Round.EventType);
            }
            else
            {
                newRound = Round;
            }

            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                newRound.SheetFormatFilename = sheet.FileInfo.Name;
                db.Update(newRound);
            }

            EventManager.RoundManager.SheetFormatManager.LoadSheet(newRound, GetOrderedPilots().ToArray(), true);
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
            return new Pilot[0];
        }
    }
}
