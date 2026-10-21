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

        protected override Size MaxRenderTargetSize
        {
            get
            {
                // Calculate required size dynamically based on number of races
                // No artificial caps - let the GPU handle its own limits
                int racesPerColumn = RoundsNode?.RacesPerColumn ?? 3;
                int raceCount = RaceNodes?.Count() ?? 0;
                int columns = raceCount > 0 ? (int)Math.Ceiling(raceCount / (float)racesPerColumn) : 1;

                // Estimate width needed per column
                int estimatedWidthPerColumn = 600;
                int requiredWidth = columns * estimatedWidthPerColumn;

                return new Size(requiredWidth, 4096);
            }
        }

        public EventRoundNode(RoundsNode roundsNode, Round round)
            : base(roundsNode.EventManager, round)
        {
            RoundsNode = roundsNode;

            MenuButton = new ImageButtonNode(@"img\settings.png", Color.Transparent, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            MenuButton.OnClick += (mie) => { ShowMenu(mie, MenuButton.Bounds.Location); };
            buttonContainer.AddChild(MenuButton, 0);

            switch (round.StageType)
            {
                case StageTypes.DoubleElimination:
                case StageTypes.Final:
                    instructionNode = new Node();
                    instructionNode.RelativeBounds = new RectangleF(0.1f, 0.3f, 0.8f, 0.2f);

                    TextNode instructions = new TextNode(Translator.Get("Label.RoundEmptyFinal", "Races will be automatically\nadded as more results come in."), Theme.Current.Rounds.Text.XNA);
                    instructions.Alignment = RectangleAlignment.Center;
                    instructionNode.AddChild(instructions);
                    break;

                default:
                    instructionNode = CreateEmptyRoundOptions();
                    break;
            }
            AddChild(instructionNode);

            EventManager.OnEventChange += Refresh;
            EventManager.OnPilotRefresh += Refresh;
            EventManager.RaceManager.OnRaceReset += RaceManager_OnRaceReset;
            EventManager.RaceManager.OnPilotAdded += OnCurrentRacePilotChange;
            EventManager.RaceManager.OnPilotRemoved += OnCurrentRacePilotChange;

            UpdateTitle();
        }

        // What an empty round offers: fill it by hand, or give it a format that generates the races.
        private Node CreateEmptyRoundOptions()
        {
            Node options = new Node();
            options.RelativeBounds = new RectangleF(0.06f, 0.12f, 0.88f, 0.8f);

            Color textColor = Theme.Current.Rounds.Text.XNA;
            Color hover = Theme.Current.Hover.XNA;
            ToolTexture background = Theme.Current.Rounds.RaceTitle;

            TextNode title = new TextNode(Translator.Get("Label.RoundEmptyTitle", "Empty Round"), textColor);
            title.RelativeBounds = new RectangleF(0, 0.01f, 1, 0.035f);
            title.Alignment = RectangleAlignment.Center;
            options.AddChild(title);

            TextNode instructions = new TextNode(Translator.Get("Label.RoundEmptyInstructions", "This round has no races yet. Fill it yourself,\nor pick a format to generate the races for you."), new Color(textColor, 0.75f));
            instructions.RelativeBounds = new RectangleF(0, 0.06f, 1, 0.055f);
            instructions.Alignment = RectangleAlignment.Center;
            options.AddChild(instructions);

            TextNode manual = new TextNode(Translator.Get("Label.RoundEmptyManual", "Fill it yourself"), textColor);
            manual.RelativeBounds = new RectangleF(0, 0.15f, 1, 0.035f);
            manual.Alignment = RectangleAlignment.Center;
            options.AddChild(manual);

            // Filling it by hand: each action with what it does beside it
            TextButtonNode addRace = ManualAction(options, 0, Translator.Get("Button.AddRace", "Add Race"),
                Translator.Get("Label.RoundEmptyAddRace", "Adds an empty race. Drag\npilots into it from the list."));
            addRace.OnClick += (mie) => { AddRace(); };

            TextButtonNode autoFill = ManualAction(options, 1, Translator.Get("Button.AutoFill", "Auto-Fill"),
                Translator.Get("Label.RoundEmptyAutoFill", "Races for every pilot not yet\nin the round, on their channels."));
            autoFill.OnClick += (mie) => { FillRound?.Invoke(Round); };

            TextButtonNode paste = ManualAction(options, 2, Translator.Get("Button.PastePilots", "Paste"),
                Translator.Get("Label.RoundEmptyPaste", "Pilot names copied as text,\none per line, or a copied round."));
            paste.OnClick += (mie) => { PastePilot?.Invoke(Round); };

            ColorNode divider = new ColorNode(new Color(textColor, 0.25f));
            divider.RelativeBounds = new RectangleF(0.05f, 0.436f, 0.9f, 0.003f);
            options.AddChild(divider);

            TextNode formatsHeading = new TextNode(Translator.Get("Label.RoundEmptyFormats", "Or use a format"), textColor);
            formatsHeading.RelativeBounds = new RectangleF(0, 0.483f, 1, 0.035f);
            formatsHeading.Alignment = RectangleAlignment.Center;
            options.AddChild(formatsHeading);

            // Formats generate the races. Each opens its list, Seeder opens its web app.
            List<IconButtonNode> formats = new List<IconButtonNode>();
            formats.Add(FormatTile(@"img\format-builtin.png", "Built-in", (menu, pilots) => AddBuiltInFormats(menu, pilots)));
            formats.Add(FormatTile(@"img\format-script.png", "Script", (menu, pilots) => AddScriptFormats(menu, pilots)));
            formats.Add(FormatTile(@"img\format-sheet.png", "Sheet", (menu, pilots) => AddSheetFormats(menu, pilots)));

            if (RoundsNode.OpenSeeder != null)
            {
                IconButtonNode seeder = FormatTileButton(@"img\seeder.png", "Seeder");
                seeder.OnClick += (mie) => { RoundsNode.OpenSeeder?.Invoke(); };
                formats.Add(seeder);
            }

            // Races from an outside system (eg MultiGP / ZippyQ), same as the round menu: only on the last round
            ExternalRaceProvider[] providers = EventManager.ExternalRaceProviders ?? new ExternalRaceProvider[0];
            if (providers.Any() && EventManager.RoundManager.GetLastRound(Round.EventType, Round.Stage) == Round)
            {
                IconButtonNode sync = FormatTileButton(@"img\format-sync.png", "Sync Down");
                sync.OnClick += (mie) =>
                {
                    if (providers.Length == 1)
                    {
                        providers[0].TriggerCreateRaces(Round);
                        return;
                    }

                    MouseMenu menu = new MouseMenu(this);
                    foreach (ExternalRaceProvider provider in providers)
                    {
                        ExternalRaceProvider p = provider;
                        menu.AddItem(Translator.Format("Menu.AddFromProvider", "Add {0}", p.Name), () => { p.TriggerCreateRaces(Round); });
                    }
                    ShowBelow(menu, sync, mie);
                };
                formats.Add(sync);
            }

            IconButtonNode search = FormatTileButton(@"img\format-search.png", "Search");
            search.OnClick += (mie) => { ShowFormatSelector(EventManager.Event.Pilots); };
            formats.Add(search);

            // About the size of the control buttons (Next etc)
            const int perRow = 3;
            const float tileHeight = 0.11f;
            const float rowGap = 0.02f;
            const float tileGap = 0.05f;
            const float tileWidth = 0.26f;
            for (int i = 0; i < formats.Count; i += perRow)
            {
                Node row = new Node();
                row.RelativeBounds = new RectangleF(0, 0.538f + (i / perRow) * (tileHeight + rowGap), 1, tileHeight);
                options.AddChild(row);

                // A short last row is centred
                IconButtonNode[] tiles = formats.Skip(i).Take(perRow).ToArray();
                float x = (1 - (tiles.Length * tileWidth + (tiles.Length - 1) * tileGap)) / 2;
                foreach (IconButtonNode tile in tiles)
                {
                    tile.RelativeBounds = new RectangleF(x, 0, tileWidth, 1);
                    row.AddChild(tile);
                    x += tileWidth + tileGap;
                }
            }

            return options;
        }

        // One manual action row: a button, with a short description of what it does beside it
        private TextButtonNode ManualAction(Node options, int index, string text, string description)
        {
            const float rowHeight = 0.06f;
            const float rowGap = 0.012f;
            float y = 0.2f + index * (rowHeight + rowGap);

            Color textColor = Theme.Current.Rounds.Text.XNA;

            TextButtonNode button = new TextButtonNode(text, Theme.Current.Rounds.RaceTitle, Theme.Current.Hover.XNA, textColor);
            button.RelativeBounds = new RectangleF(0, y, 0.3f, rowHeight);

            // Same share of the height each, so short labels don't come out bigger than long ones
            button.TextNode.RelativeBounds = new RectangleF(0.06f, 0.25f, 0.88f, 0.5f);
            options.AddChild(button);

            TextNode descriptionNode = new TextNode(description, new Color(textColor, 0.75f));
            descriptionNode.RelativeBounds = new RectangleF(0.34f, y + rowHeight * 0.1f, 0.66f, rowHeight * 0.8f);
            descriptionNode.Alignment = RectangleAlignment.CenterLeft;
            options.AddChild(descriptionNode);

            return button;
        }

        // A format tile that opens a menu of that kind of format
        private IconButtonNode FormatTile(string icon, string name, Action<MouseMenu, IEnumerable<Pilot>> fillMenu)
        {
            IconButtonNode tile = FormatTileButton(icon, name);
            tile.OnClick += (mie) =>
            {
                MouseMenu menu = new MouseMenu(this);
                fillMenu(menu, EventManager.Event.Pilots);
                if (!menu.Items.Any())
                {
                    menu.AddItem(Translator.Get("Label.NoFormats", "None found"), () => { });
                }
                ShowBelow(menu, tile, mie);
            };
            return tile;
        }

        // The round is drawn through a render target; Translation takes the node back to screen space
        private static void ShowBelow(MouseMenu menu, Node node, MouseInputEvent mie)
        {
            Rectangle bounds = node.Bounds;
            bounds.Location -= mie.Translation;
            menu.Show(new Point(bounds.X, bounds.Bottom));
        }

        private IconButtonNode FormatTileButton(string icon, string name)
        {
            IconButtonNode tile = new IconButtonNode(icon, Translator.Get("Button." + name, name), Theme.Current.Rounds.RaceTitle, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);

            // Keep the icon's own colours; padded above so it sits evenly in the tile
            tile.ImageNode.Tint = Color.White;
            tile.ImageNode.RelativeBounds = new RectangleF(0, 0.1f, 1, 0.56f);
            tile.TextNode.RelativeBounds = new RectangleF(0.05f, 0.7f, 0.9f, 0.24f);
            return tile;
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
            if (Round.StageType != StageTypes.Default)
            {
                string a = Round.StageType.ToString();
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
                else
                {
                    rn.Race = race;
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

            string headingText = RaceStringFormatter.Instance.RoundToString(Round);
            if (Round.Handicapped) headingText += "  (Handicap)";
            SetHeading(headingText);
            UpdateButtons();
            RequestLayout();
        }

        private void ShowMenu(MouseInputEvent mie, Point position)
        {
            string clipboardText = PlatformTools.Clipboard.GetText();
            int pasteRaceCount = 0;
            if (PastedRace.TryParsePastedRaces(clipboardText, out List<PastedRace> jsonRaces))
            {
                pasteRaceCount = jsonRaces.Count(r => r.Pilots != null && r.Pilots.Any());
            }
            else
            {
                var lines = PlatformTools.Clipboard.GetLines();
                int pastePilotCount = EventManager.GetPilotsFromLines(lines, true).Count();
                if (pastePilotCount > 0 && pastePilotCount <= EventManager.Channels.Length)
                    pasteRaceCount = 1;
                else if (pastePilotCount > EventManager.Channels.Length)
                    pasteRaceCount = 2;
            }

            MouseMenu mm = new MouseMenu(this);

            mm.AddItem("Add Race", AddRace);

            if (canFill)
            {
                mm.AddItem("Auto-Fill Round", () => { FillRound?.Invoke(Round); });
            }

            if (EventManager.ExternalRaceProviders != null)
            {
                if (EventManager.RoundManager.GetLastRound(Round.EventType, Round.Stage) == Round)
                {
                    foreach (var external in EventManager.ExternalRaceProviders)
                    {
                        var t = external;
                        mm.AddItem(Translator.Format("Menu.AddFromProvider", "Add {0}", external.Name), () => { t.TriggerCreateRaces(Round); });
                    }
                }
            }

            if (!hasRace)
            {
                MouseMenu formatMenu = mm.AddSubmenu("Set Format");
                AddFormatMenu(formatMenu, EventManager.Event.Pilots);
            }

            if (hasRace)
            {
                mm.AddItem("Copy Round", CopyPilots);
            }

            if (pasteRaceCount == 1)
            {
                mm.AddItem("Paste Race", PasteRace);
            }

            if (canPasteAll && pasteRaceCount > 1)
            {
                mm.AddItem("Paste Round", () => { PastePilot?.Invoke(Round); });
            }

            if (Races.Any())
            {
                mm.AddItem("Set rounds channels back to pilots list", () => { MatchChannels?.Invoke(Races); });
            }

            mm.AddItem("Edit Round", EditRound);
            if (Round.Stage == null)
            {
                Stage[] deletedStages = EventManager.RoundManager.GetDeletedStages();
                if (deletedStages.Any())
                {
                    MouseMenu restoreMenu = mm.AddSubmenu("Restore Stage");
                    foreach (Stage stage in deletedStages)
                    {
                        Stage local = stage;
                        restoreMenu.AddItem(string.IsNullOrEmpty(local.Name) ? "(no name)" : local.Name, () =>
                        {
                            EventManager.RoundManager.RestoreStage(Round, local);
                            Refresh(true);
                        });
                    }
                }
            }
            else
            {
                mm.AddItem("Edit Stage", EditStage);

                mm.AddItem("Remove Round from Stage", () =>
                {
                    PopupLayer pl = GetLayer<PopupLayer>();
                    pl.PopupConfirmation("Remove from Stage?", () =>
                    {
                        LoadingLayer ll = GetLayer<LoadingLayer>();
                        ll.WorkQueue.Enqueue("Deleting stage", () =>
                        {
                            EventManager.RoundManager.RemoveStage(Round);
                            Refresh(true);
                        });
                    });
                });

                mm.AddItemConfirm("Remove Stage", () =>
                {
                    EventManager.RoundManager.DeleteStage(Round.Stage);
                });

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

                foreach (EventTypes t in EventManager.GetEventTypes())
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
            string text = PlatformTools.Clipboard.GetText();
            if (PastedRace.TryParsePastedRaces(text, out List<PastedRace> jsonRaces))
            {
                PastedRace first = jsonRaces.FirstOrDefault(r => r.Pilots != null && r.Pilots.Any());
                if (first != null)
                {
                    EventManager.RoundManager.SetRoundPilots(Round, new PastedRace[] { first });
                }
            }
            else
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
            canAddFinal = Round.StageType != StageTypes.Final;
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
