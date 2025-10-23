using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using RaceLib.Game;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Video;

namespace UI.Nodes.Rounds
{
    public class EventRoundNode : EventXNode
    {
        public IEnumerable<EventRaceNode> RaceNodes { get { return contentContainer.Children.OfType<EventRaceNode>(); } }

        public ImageButtonNode MenuButton { get; private set; }

        public event RoundDelegate FillRound;
        public event RoundDelegate PastePilot;

        public event Action<EventTypes, Round> SetRaceTypes;

        public event Action<IEnumerable<Race>> MatchChannels;

        public IEnumerable<Race> Races { get { return RaceNodes.Select(r => r.Race); } }

        private bool canFill;
        private bool canPasteAll;
        private bool canRemove;
        private bool hasRace;

        private Node instructionNode;

        public event Action NeedFullRefresh;

        public RoundsNode RoundsNode { get; private set; }

        public EventRoundNode(RoundsNode roundsNode, Round round)
            : base(roundsNode.EventManager, round)
        {
            RoundsNode = roundsNode;

            MenuButton = new ImageButtonNode(@"img\settings.png", Color.Transparent, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            MenuButton.OnClick += (mie) => { ShowMenu(mie, MenuButton.Bounds.Location); };
            buttonContainer.AddChild(MenuButton, 0);

            instructionNode = new Node();
            instructionNode.RelativeBounds = new RectangleF(0.1f, 0.3f, 0.8f, 0.2f);

            string instructionText;

            switch (round.RoundType)
            {
                case Round.RoundTypes.DoubleElimination:
                case Round.RoundTypes.Final:
                    instructionText = Translator.Get("Label.RoundEmptyFinal", "Races will be automatically\nadded as more results come in.");
                    break;
                default:
                    instructionText = Translator.Get("Label.RoundEmpty", "Right click here to add a races.\nDrag and drop pilots to fill the race.\nAuto-Fill to generate a new round\nPaste pilots from clipboard");
                    break;
            }

            TextNode instructions = new TextNode(instructionText, Theme.Current.Rounds.Text.XNA);
            instructions.Alignment = RectangleAlignment.Center;
            instructionNode.AddChild(instructions);
            AddChild(instructionNode);

            EventManager.OnEventChange += Refresh;
            EventManager.OnPilotRefresh += Refresh;
            EventManager.RaceManager.OnRaceReset += RaceManager_OnRaceReset;
            EventManager.RaceManager.OnPilotAdded += OnCurrentRacePilotChange;
            EventManager.RaceManager.OnPilotRemoved += OnCurrentRacePilotChange;

            UpdateTitle();
        }

        private void OnCurrentRacePilotChange(PilotChannel pilot)
        {
            if (Races.Contains(EventManager.RaceManager.CurrentRace))
            {
                Refresh();
            }
        }

        public override void Dispose()
        {
            EventManager.OnEventChange -= Refresh;
            EventManager.OnPilotRefresh -= Refresh;
            EventManager.RaceManager.OnRaceReset -= RaceManager_OnRaceReset;

            base.Dispose();
        }

        private void RaceManager_OnRaceReset(Race race)
        {
            UpdateButtons();

            if (Races.Contains(race))
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            Refresh(false);
        }

        public void Refresh(bool full)
        {
            if (full)
            {
                NeedFullRefresh?.Invoke();
            }
            else
            {
                Race[] races = RaceNodes.Select(rn => rn.Race).ToArray();
                SetRaces(races);
            }
        }

        private void UpdateTitle()
        {
            if (Round.RoundType != Round.RoundTypes.Round)
            {
                string a = Round.RoundType.ToString();
                SetSubHeading(a.CamelCaseToHuman());
            }
            else
            {
                if (Round.Stage != null && Round.Stage.HasSheetFormat)
                {
                    SetSubHeading(Round.Stage.Name);
                }
                else
                {
                    SetSubHeading("");
                }
            }
        }

        public void SetRaces(IEnumerable<Race> races)
        {
            hasRace = races.Any();

            UpdateTitle();

            instructionNode.Visible = !races.Any();

            foreach (Race race in races)
            {
                EventRaceNode rn = RaceNodes.FirstOrDefault(ran => ran.Race == race);
                if (rn == null)
                {
                    rn = new EventRaceNode(EventManager, race);
                    if (RoundsNode.RacesPerColumn < 3)
                    {
                        rn.AspectRatio = EventRaceNode.StandardAspectRatio * 2 / 3.0f;
                    }

                    rn.NeedRefresh += () => { Refresh(); };
                    rn.NeedFullRefresh += () => { Refresh(true); };
                    contentContainer.AddChild(rn);
                }

                // Sync the in memory round objects..
                race.Round = Round;
            }

            foreach (EventRaceNode rn in RaceNodes.ToArray())
            {
                if (!races.Contains(rn.Race))
                {
                    rn.Dispose();
                }

                rn.NeedsInit = true;
            }

            int racesPerColumn = RoundsNode.RacesPerColumn;

            int columns = (int)Math.Ceiling(races.Count() / (float)racesPerColumn);
            if (columns == 0)
                columns = 1;

            float width = 1.0f / columns;

            int column = 0;

            List<EventRaceNode> current = new List<EventRaceNode>();
            foreach (EventRaceNode rn in RaceNodes.OrderBy(rn => rn.Race.RaceNumber))
            {
                current.Add(rn);
                if (current.Count == racesPerColumn)
                {
                    float leftAlign = column * width;
                    MakeColumns(current, racesPerColumn, leftAlign, width);
                    column++;
                    current.Clear();
                }
            }

            if (current.Any())
            {
                float leftAlign = column * width;
                MakeColumns(current, racesPerColumn, leftAlign, width);
            }

            AspectRatio = 0.4f * columns;

            SetHeading(RaceStringFormatter.Instance.RoundToString(Round));
            UpdateButtons();
            RequestLayout();
        }

        private void ShowMenu(MouseInputEvent mie, Point position)
        {
            var lines = PlatformTools.Clipboard.GetLines();
            int pastePilotCount = EventManager.GetPilotsFromLines(lines, true).Count();

            MouseMenu mm = new MouseMenu(this);

            mm.AddItem("Add Race", AddRace);

            if (canFill)
            {
                mm.AddItem("Auto-Fill Round", () => { FillRound?.Invoke(Round); });
            }

            if (EventManager.ExternalRaceProviders != null)
            {
                if (EventManager.RoundManager.GetLastRound(Round.EventType, Round.RoundType) == Round)
                {
                    foreach (var external in EventManager.ExternalRaceProviders)
                    {
                        var t = external;
                        mm.AddItem("Add " + external.Name, () => { t.TriggerCreateRaces(Round); });
                    }
                }
            }

            if (!hasRace)
            {
                MouseMenu addFormat = mm.AddSubmenu("Set Format");
                AddFormatMenu(addFormat);
            }

            if (hasRace)
            {
                mm.AddItem("Copy Round", CopyPilots);
            }

            if (pastePilotCount > 0 && pastePilotCount <= EventManager.Channels.Length)
            {
                mm.AddItem("Paste Race", PasteRace);
            }

            if (canPasteAll && pastePilotCount > EventManager.Channels.Length)
            {
                mm.AddItem("Paste Round", () => { PastePilot?.Invoke(Round); });
            }

            if (Races.Any())
            {
                mm.AddItem("Set rounds channels back to pilots list", () => { MatchChannels?.Invoke(Races); });
            }

            mm.AddItem("Edit Round", EditRound);


            if (Round.Stage != null)
            {
                mm.AddItem("Edit Stage", EditStage);
            }

            if (Round.Stage != null)
            {
                mm.AddItem("Delete Stage and contents", () =>
                {
                    PopupLayer pl = GetLayer<PopupLayer>();
                    pl.PopupConfirmation("Delete Stage and contents (except finished races)", () => 
                    {
                        LoadingLayer ll = GetLayer<LoadingLayer>();
                        ll.WorkQueue.Enqueue("Deleting stage", () =>
                        {
                            EventManager.RoundManager.DeleteStageAndContents(Round.Stage);
                        });
                    });
                });
            }

            if (!EventManager.Event.RulesLocked)
            {
                MouseMenu typeMenu = mm.AddSubmenu("Set Type");

                foreach (EventTypes t in Event.GetEventTypes())
                {
                    EventTypes typee = t;

                    if (typee == EventTypes.Game)
                    {
                        try
                        {

                            GameType[] gameTypes = GameType.Read(EventManager.Profile);
                            if (gameTypes.Any())
                            {
                                var subMenu = typeMenu.AddSubmenu("Game");
                                foreach (GameType gameType in gameTypes)
                                {
                                    var gt = gameType;
                                    subMenu.AddItem(gameType.Name, () => { SetGameType(gt); });
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.UI.LogException(this, e);
                        }
                    }
                    else
                    {
                        string typeString = RaceStringFormatter.Instance.GetEventTypeText(typee);
                        typeMenu.AddItem(typeString, () => { SetType(typee, Round); });
                    }
                }
            }

            var sheet = EventManager.RoundManager.SheetFormatManager.GetRoundSheetFormat(Round);
            if (sheet != null)
            {
                mm.AddBlank();

                mm.AddItem("Regenerate Round from Sheet", () =>
                {
                    sheet.GenerateSingleRound(Round);
                });

                mm.AddItem("View Sheet Contents", () =>
                {
                    BaseGame baseGame = CompositorLayer.Game as BaseGame;
                    baseGame.ShowNewWindow(new SheetNode(sheet));
                });

                mm.AddItem("Export Sheet Contents", () =>
                {
                    string filename = PlatformTools.SaveFileDialog("Export Sheet Contents", "XLSX|*.xlsx");
                    if (!string.IsNullOrEmpty(filename))
                    {
                        try
                        {
                            sheet.Save(filename);
                        }
                        catch (Exception ex)
                        {
                            GetLayer<PopupLayer>().PopupMessage("Couldn't save. " + ex.Message);
                        }
                    }
                });
            }


            if (hasRace)
            {
                if (EventManager.RaceManager.TimingSystemManager.HasDummyTiming)
                {
                    mm.AddBlank();
                    mm.AddItem("Generate Dummy Round Results", () =>
                    {
                        GenerateDummyResults();
                    });
                }
            }

            mm.Show(position - mie.Translation);
        }

        private void CopyPilots()
        {
            List<string> lines = new List<string>();
            foreach (Race race in Races)
            {
                foreach (var c in EventManager.Channels.GetChannelGroups())
                {
                    Pilot p = race.GetPilot(c);
                    if (p == null)
                    {
                        lines.Add("");
                    }
                    else
                    {
                        lines.Add(p.Name);
                    }
                }
            }

            PlatformTools.Clipboard.SetLines(lines);
        }

        private void SetType(EventTypes type, Round round)
        {
            SetRaceTypes?.Invoke(type, Round);
            contentContainer.ClearDisposeChildren();
            Refresh(true);
        }

        private void SetGameType(GameType gameType)
        {
            SetRaceTypes?.Invoke(EventTypes.Game, Round);
            EventManager.GameManager.SetGameType(gameType);

            if (gameType != null)
            {
                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    Round.GameTypeName = gameType.Name;
                    db.Update(Round);
                }
            }
            else
            {
                Round.GameTypeName = "";
            }
            
            Refresh(true);
        }

        private void GenerateDummyResults()
        {
            var timing = EventManager.RaceManager.TimingSystemManager.PrimeSystems.OfType<Timing.DummyTimingSystem>().FirstOrDefault();
            foreach (Race race in EventManager.RaceManager.GetRaces(Round))
            {
                EventManager.RaceManager.GenerateResults(timing, race, true);
            }
            Refresh();
        }

        private void PasteRace()
        {
            var lines = PlatformTools.Clipboard.GetLines();
            IEnumerable<Tuple<Pilot, Channel>> pilotChannels = EventManager.GetPilotsFromLines(lines, true);
            if (pilotChannels.Any())
            {
                Race race = EventManager.RaceManager.AddRaceToRound(Round);

                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    foreach (var kvp in pilotChannels)
                    {
                        race.SetPilot(db, kvp.Item2, kvp.Item1);
                    }
                }
            }
            Refresh(true);
        }

        private void EditRound()
        {
            ObjectEditorNode<Round> editor = new ObjectEditorNode<Round>(Round);
            GetLayer<PopupLayer>().Popup(editor);
            editor.OnOK += (r) =>
            {
                if (editor.Selected != null)
                {
                    using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                    {
                        db.Upsert(editor.Selected);
                    }
                    Refresh(true);
                }
            };
        }


        public void EditStage()
        {
            ObjectEditorNode<Stage> editor = new ObjectEditorNode<Stage>(Round.Stage);
            GetLayer<PopupLayer>().Popup(editor);
            editor.OnOK += (r) =>
            {
                if (editor.Selected != null)
                {
                    using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                    {
                        db.Upsert(editor.Selected);
                    }
                    RoundsNode.Refresh();
                }
            };
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (base.OnMouseInput(mouseInputEvent))
            {
                return true;
            }

            if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                ShowMenu(mouseInputEvent, mouseInputEvent.Position);
                return true;
            }

            if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Pressed)
            {
                MouseInputEvent translated = Translate(mouseInputEvent);
                if (heading.Contains(translated.Position))
                {
                    GetLayer<DragLayer>()?.RegisterDrag(this, mouseInputEvent);
                }
            }

            return false;
        }

        protected override void UpdateButtons()
        {
            base.UpdateButtons();
            if (MenuButton != null)
                MenuButton.Scale(0.6f);

            bool onlyRound = EventManager.RoundManager.Rounds.Length == 1;
            bool hasRaces = Races.Any();
            bool hasFinishedRaces = Races.Any(r => r.Ended);
            canRemove = (!onlyRound || hasRaces) && !hasFinishedRaces;

            canPasteAll = !Races.Any(r => r.Ended);

            canFill = !EventManager.RoundManager.DoesRoundHaveAllPilots(Round);
            canAddLapCount = true;
            canSum = true;
            canClone = true;
            canAddTimes = true;
            canAddFinal = Round.RoundType != Round.RoundTypes.Final;
            canAddRace = true;

            RemoveRoundButton.Visible = canRemove;
        }
        
        public override bool IsRoundInStage()
        {
            return Round.Stage != null;
        }

        public override IEnumerable<Pilot> GetOrderedPilots()
        {
            foreach (Race race in Races.OrderBy(r => r.RaceOrder))
            {
                foreach (Pilot pilot in race.Pilots)
                {
                    yield return pilot;
                }
            }
        }

        public override Rectangle? CanDrop(MouseInputEvent mouseInputEvent, Node node)
        {
            EventRaceNode draggedRaceNode = node as EventRaceNode;
            if (draggedRaceNode != null)
            {
                MouseInputEvent translated = Translate(mouseInputEvent);

                Node n = FindDragTarget(translated);
                Point location;
                if (n == null)
                {
                    n = RaceNodes.OrderBy(r => r.Race.RaceNumber).LastOrDefault();
                    if (n != null)
                    {
                        location = n.Bounds.Location;
                        location.Y += n.Bounds.Height + 10;
                    }
                    else
                    {
                        location = contentContainer.Bounds.Location;
                    }
                }
                else
                {
                    location = n.Bounds.Location;
                }

                Rectangle output = new Rectangle(location, draggedRaceNode.Bounds.Size);
                output.Height = 2;
                output.Y -= 5;

                return TranslateBack(output);
            }

            return base.CanDrop(mouseInputEvent, node);
        }

        public override bool OnDrop(MouseInputEvent mouseInputEvent, Node node)
        {
            MouseInputEvent translated = Translate(mouseInputEvent);

            if (node.Contains(translated.Position))
            {
                if (node.ParentChain.Contains(this))
                {
                    return false;
                }
            }

            EventRaceNode draggedRaceNode = node as EventRaceNode;
            if (draggedRaceNode != null)
            {
                Race draggedRace = draggedRaceNode.Race;
                if (draggedRace != null)
                {
                    using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                    {
                        draggedRace.Round = Round;

                        Node found = FindDragTarget(translated);

                        int number = 1;
                        foreach (EventRaceNode racenode in RaceNodes.OrderBy(r => r.Race.RaceNumber))
                        {
                            if (racenode == draggedRaceNode)
                                continue;

                            if (racenode == found)
                            {
                                draggedRace.RaceNumber = number;
                                number++;
                            }

                            racenode.Race.RaceNumber = number;
                            number++;
                        }

                        if (found == null)
                        {
                            draggedRace.RaceNumber = number;
                        }

                        Race[] races = RaceNodes.Select(rn => rn.Race).ToArray();

                        db.Update(races);
                        db.Update(draggedRace);
                    }
                }
                contentContainer.ClearDisposeChildren();

                NeedFullRefresh?.Invoke();
            }

            return base.OnDrop(mouseInputEvent, node);
        }

        public EventRaceNode FindDragTarget(MouseInputEvent translated)
        {
            foreach (EventRaceNode racenode in RaceNodes.OrderBy(r => r.Race.RaceNumber))
            {
                if (racenode.Bounds.Bottom > translated.Position.Y
                             && racenode.Bounds.Right > translated.Position.X
                             && racenode.Bounds.Left < translated.Position.X)
                {
                    return racenode;
                }
            }

            return null;
        }
    }
}
