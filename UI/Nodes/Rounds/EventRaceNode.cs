using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Format;
using Sound;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes.Rounds
{
    public class EventRaceNode : AspectNode
    {
        public Race Race { get; private set; }

        public EventManager EventManager { get; private set; }

        private Node container;
        private TextButtonNode heading;

        public IEnumerable<PilotRaceInfoNode> PilotRaceInfoNodes { get { return container.Children.OfType<PilotRaceInfoNode>(); } }

        public event Action NeedRefresh;
        public event Action NeedFullRefresh;

        public bool NeedsInit { get; set; }

        public const float StandardAspectRatio = 1.4f;

        public EventRaceNode(EventManager eventManager, Race race)
        {
            AspectRatio = StandardAspectRatio;
            EventManager = eventManager;
            Race = race;

            NeedsInit = true;
            container = new Node();
            AddChild(container);
        }

        private void Init()
        {
            lock (container)
            {
                container.ClearDisposeChildren();

                RoundSheetFormat sheetFormat = EventManager.RoundManager.SheetFormatManager.GetRoundSheetFormat(Race.Round);
                int pilotCount = Race.PilotCount;

                if (heading == null)
                {
                    heading = new TextButtonNode(Race.RaceName, Theme.Current.Rounds.RaceTitle, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
                    heading.OnClick += Heading_OnClick;
                    AddChild(heading);
                }
                float headingHeight = 0.15f;

                // Make the heading shrink as the Aspect ratio becomes non-standard
                headingHeight *= AspectRatio / StandardAspectRatio;

                heading.RelativeBounds = new RectangleF(0, 0.0f, 1, headingHeight);
                heading.TextNode.Alignment = RectangleAlignment.BottomLeft;

                float headingBottom = heading.RelativeBounds.Bottom + 0.01f;

                container.RelativeBounds = new RectangleF(0, headingBottom, 1, 1 - headingBottom);

                List<Channel> channels = EventManager.Channels.Where(c => c != null).ToList();
                channels.AddRange(Race.Channels.Where(c => c != null && !channels.Contains(c)));

                Channel[][] grouped = channels.GetChannelGroups().ToArray();
                foreach (Channel[] shared in grouped)
                {
                    Channel channel = shared.FirstOrDefault();
                    if (channel == null)
                        continue;

                    bool channelChanged = false;

                    Pilot pilot = null;
                    PilotChannel pilotChannel = Race.GetPilotChannel(channel);
                    if (pilotChannel != null)
                    {
                        pilot = pilotChannel.Pilot;
                        channel = pilotChannel.Channel;
                        Race previousRace = EventManager.RaceManager.GetPreviousRace(pilot, Race);
                        if (previousRace != null)
                        {
                            channelChanged = previousRace.GetChannel(pilot) != channel;
                        }
                    }

                    if (pilot == null && sheetFormat != null && pilotCount == sheetFormat.ChannelCount)
                    {
                        continue;
                    }

                    PilotRaceInfoNode pilotRaceInfoNode = new PilotRaceInfoNode(this, pilot, channel, channelChanged, shared.Where(c => c != channel));
                    container.AddChild(pilotRaceInfoNode);

                    pilotRaceInfoNode.ResultText = EventManager.ResultManager.GetResultText(Race, pilot);
                }

                int size = Math.Max(grouped.Count(), 6);

                AlignVertically(0.0f, size, PilotRaceInfoNodes.ToArray());

                foreach (var prin in PilotRaceInfoNodes)
                {
                    prin.Scale(0.98f, 0.9f);
                }
                heading.Scale(0.98f, 1);

                NeedsInit = false;
            }
        }

        private void Heading_OnClick(MouseInputEvent mie)
        {
            bool isDragging = false;

            DragLayer dragLayer = GetLayer<DragLayer>();
            if (dragLayer != null)
            {
                isDragging = dragLayer.IsDragging;
            }
            if (!isDragging)
            {
                if (mie.ButtonState == ButtonStates.Released)
                {
                    EventManager.RaceManager.SetRace(Race);
                }
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                Pilot pilot = null;

                foreach (var pin in PilotRaceInfoNodes)
                {
                    if (pin.Contains(mouseInputEvent.Position))
                    {
                        pilot = pin.Pilot;
                    }
                }

                bool hasStarted = Race.Start != DateTime.MinValue;

                MouseMenu mm = new MouseMenu(this);

                if (EventManager.RaceManager.CurrentRace != Race)
                {
                    mm.AddItem("Open Race", () => { EventManager.RaceManager.SetRace(Race); });
                }

                if (Race != null)
                {
                    if (Race.Ended)
                    {
                        mm.AddItem("Announce Results", () => { SoundManager.Instance.AnnounceResults(Race); });
                    }
                    else
                    {
                        mm.AddItem("Announce Race / Pilots", () => { SoundManager.Instance.AnnounceRace(Race, true); });
                        mm.AddItem("Hurry Up Everyone", () => { SoundManager.Instance.HurryUpEveryone(); });
                    }
                }

                mm.AddBlank();

                mm.AddItem("Edit Race", () =>
                {
                    RaceEditor editor = new RaceEditor(EventManager, Race);
                    GetLayer<PopupLayer>().Popup(editor);
                    editor.OnOK += (r) => { Refresh(true); };
                });

                MouseMenu copyMenu = mm.AddSubmenu("Copy...");

                bool pilotsToCopy = Race.PilotCount > 0;

                if (pilotsToCopy)
                {
                    copyMenu.AddItem("Copy Pilots", () =>
                    {
                        CopyToClipboard();
                    });
                }

                if (hasStarted)
                {
                    if (pilotsToCopy)
                    {
                        MouseMenu top = copyMenu.AddSubmenu("Copy Top Pilots");
                        MouseMenu bottom = copyMenu.AddSubmenu("Copy Bottom Pilots");

                        var ordered = EventManager.ResultManager.GetResults(Race).OrderBy(r => r.Position);

                        int topi = 1;
                        int bottomi = ordered.Count();

                        foreach (var result in ordered)
                        {
                            int localTop = topi;
                            int localBottom = bottomi;

                            top.AddItem("Copy Top " + (topi > 1 ? topi + " pilots" : "pilot"), () => { CopyTop(localTop); });
                            bottom.AddItem("Copy Bottom " + (bottomi > 1 ? bottomi + " pilots" : "pilot"), () => { CopyBottom(localBottom); });
                            bottomi--;
                            topi++;
                        }
                    }

                    copyMenu.AddItem("Copy Results", () =>
                    {
                        string textResults = EventManager.ResultManager.GetResultsText(Race, ApplicationProfileSettings.Instance.Units);
                        PlatformTools.Clipboard.SetText(textResults);
                    });

                    mm.AddItemConfirm("Reset Race", () => { EventManager.RaceManager.ResetRace(Race); });
                }
                else
                {
                    var lines = PlatformTools.Clipboard.GetLines();
                    IEnumerable<Tuple<Pilot, Channel>> pilotChannels = EventManager.GetPilotsFromLines(lines, false);

                    if (pilotChannels.Any())
                    {
                        mm.AddItem("Paste Pilots", () =>
                        {
                            PasteFromClipboard(false);
                        });

                        mm.AddItem("Paste Pilots (channel ordered)", () =>
                        {
                            PasteFromClipboard(true);
                        });
                    }

                    mm.AddItemConfirm("Clear Race", () => { EventManager.RaceManager.ClearRace(Race); SyncSheetChange(); Refresh(); });
                }

                mm.AddItemConfirm("Delete Race", () => { EventManager.RaceManager.RemoveRace(Race, false); SyncSheetChange(); Refresh(); });

                mm.AddSubmenu("Set Race Bracket", SetBracket, Enum.GetValues(typeof(Brackets)).OfType<Brackets>().ToArray());
                mm.AddItem("Open Race Folder", () =>
                {
                    PlatformTools.OpenFileManager(Directory.GetCurrentDirectory() + "\\events\\" + EventManager.EventId + "\\" + Race.ID + "\\");
                });
                if (pilot != null)
                {
                    mm.AddBlank();

                    List<Channel> channels = Race.GetChannel(pilot).GetInterferringChannels(EventManager.Event.Channels).ToList();
                    channels.AddRange(Race.GetFreeFrequencies(EventManager.Event.Channels));

                    if (!hasStarted && channels.Any())
                    {
                        mm.AddSubmenu("Set Pilot Channel", (c) => { SetChannel(c, pilot); }, channels.ToArray());
                    }

                    if (Race.Type.HasPoints())
                    {
                        List<string> results = new List<string>();

                        var grouped = EventManager.Event.Channels.GetChannelGroups();

                        for (int i = 1; i <= grouped.Count(); i++)
                        {
                            results.Add(i.ToStringPosition());
                        }
                        results.Add("DNF");

                        mm.AddSubmenu("Set Pilot Result", (r) => { SetResult(r, pilot); }, results.ToArray());
                    }

                    mm.AddItem("Edit Pilot", () =>
                    {
                        PilotEditor editor = new PilotEditor(EventManager, new Pilot[] { pilot });
                        GetLayer<PopupLayer>().Popup(editor);
                        Refresh();
                    });
                    mm.AddItem("Remove Pilot ",
                        () =>
                        {
                            if (EventManager.RaceManager.RemovePilot(Race, pilot) != null)
                            {
                                SyncSheetChange();
                                Refresh();
                            }
                            else
                            {
                                GetLayer<PopupLayer>().PopupMessage("Can't remove Pilot from a race that has finished");
                            }
                        });
                }

                mm.CollapseShortSubmenus();


                if (EventManager.RaceManager.TimingSystemManager.HasDummyTiming)
                {
                    mm.AddBlank();
                    mm.AddItem("Generate Dummy Results", () =>
                    {
                        EventManager.RaceManager.GenerateResults(EventManager.RaceManager.TimingSystemManager.PrimeSystems.OfType<Timing.DummyTimingSystem>().FirstOrDefault(), Race);
                        Refresh();
                    });
                }

                mm.Show(mouseInputEvent);
                return true;
            }

            if (!base.OnMouseInput(mouseInputEvent))
            {
                if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Pressed)
                {
                    if (heading.Contains(mouseInputEvent.Position))
                    {
                        GetLayer<DragLayer>()?.RegisterDrag(this, mouseInputEvent);
                    }
                    return true;
                }
                return false;
            }
            return true;
        }

        private void CopyTop(int number)
        {
            var ordered = EventManager.ResultManager.GetResults(Race).OrderBy(r => r.Position).Take(number);
            PlatformTools.Clipboard.SetLines(ordered.Select(r => r.Pilot.Name));
        }

        private void CopyBottom(int number)
        {
            var ordered = EventManager.ResultManager.GetResults(Race).OrderBy(r => r.Position).Reverse().Take(number);
            PlatformTools.Clipboard.SetLines(ordered.Select(r => r.Pilot.Name));
        }

        private void SetResult(string result, Pilot pilot)
        {
            ResultManager pointsManager = EventManager.ResultManager;

            var grouped = EventManager.Event.Channels.GetChannelGroups();
            for (int position = 1; position <= grouped.Count(); position++)
            {
                string strPosition = position.ToStringPosition();

                if (strPosition == result)
                {
                    int points = pointsManager.GetPoints(position);
                    pointsManager.SetResult(Race, pilot, position, points, false);

                    return;
                }
            }


            //DNF
            int dnfPosition = grouped.Count();
            int dnfPoints = pointsManager.PointsSettings.DNFPoints;

            pointsManager.SetResult(Race, pilot, dnfPosition, dnfPoints, true);
        }

        private void SetChannel(Channel c, Pilot p)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                Race.RemovePilot(db, p);
                Race.SetPilot(db, c, p);
                SyncSheetChange();
            }
            Refresh(true);
        }

        private void CopyToClipboard()
        {
            List<string> lines = new List<string>();
            foreach (var c in Race.Event.Channels.GetChannelGroups())
            {
                Pilot p = Race.GetPilot(c);
                if (p == null)
                {
                    lines.Add("");
                }
                else
                {
                    lines.Add(p.Name);
                }
            }

            PlatformTools.Clipboard.SetLines(lines);
        }

        private void PasteFromClipboard(bool assign)
        {
            var lines = PlatformTools.Clipboard.GetLines();
            IEnumerable<Tuple<Pilot, Channel>> pcs = EventManager.GetPilotsFromLines(lines, assign);

            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                foreach (Tuple<Pilot, Channel> pc in pcs)
                {
                    Pilot p = pc.Item1;
                    Channel c = pc.Item2;

                    if (!assign && c != null)
                    {
                        if (!Race.IsFrequencyFree(c))
                        {
                            c = Race.GetFreeFrequencies(EventManager.Channels.Where(c => c.Band == c.Band)).FirstOrDefault();
                            if (c == null)
                                continue;
                        }
                    }

                    Race.SetPilot(db, c, p);
                }
            }

            SyncSheetChange();

            Refresh();
        }

        private void SetBracket(Brackets bracket)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                Race.Bracket = bracket;
                db.Update(Race);
            }

            Refresh();
        }

        public override void Layout(RectangleF parentBounds)
        {
            if (NeedsInit)
            {
                Init();
            }
            base.Layout(parentBounds);
        }


        public void Refresh(bool full = false)
        {
            heading.TextNode.Text = Race.RaceName;

            if (full)
            {
                NeedFullRefresh?.Invoke();
            }
            else
            {
                RequestLayout();
                NeedRefresh?.Invoke();
            }
        }

        public void SyncSheetChange()
        {
            RoundSheetFormat sheetFormat = EventManager.RoundManager.SheetFormatManager.GetRoundSheetFormat(Race.Round);
            if (sheetFormat == null)
                return;

            sheetFormat.SyncRace(Race);
        }
    }

    public class PilotRaceInfoNode : ColorNode, IPilot
    {
        private PilotChannelNode pilotNode;
        private TextNode resultNode;

        public Pilot Pilot { get; private set; }
        public Channel Channel { get; private set; }
        public Channel[] SharedChannels { get; private set; }

        public string ResultText
        {
            get
            {
                return resultNode.Text;
            }
            set
            {
                resultNode.Text = value;
            }
        }

        public EventRaceNode EventRaceNode { get; private set; }

        public PilotRaceInfoNode(EventRaceNode raceNode, Pilot pilot, Channel channel, bool channelChanged, IEnumerable<Channel> shared)
         : base(Theme.Current.Rounds.Foreground)
        {
            EventRaceNode = raceNode;
            Pilot = pilot;
            Channel = channel;
            SharedChannels = shared.ToArray();

            pilotNode = new PilotChannelNode(EventRaceNode.EventManager, ToolTexture.Transparent, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA, Theme.Current.Rounds.Channel);
            pilotNode.SetPilotChannel(pilot, channel, shared);
            pilotNode.RelativeBounds = new RectangleF(0, 0, 0.85f, 1);
            pilotNode.PilotNameNode.TextNode.Alignment = RectangleAlignment.CenterLeft;
            pilotNode.ChannelChangeNode.Visible = channelChanged;

            pilotNode.PilotNameNode.HoverNode.Enabled = false;
            pilotNode.ChannelNode.HoverNode.Enabled = false;

            AddChild(pilotNode);

            float y = pilotNode.PilotNameNode.TextNode.RelativeBounds.Y;
            float height = pilotNode.PilotNameNode.TextNode.RelativeBounds.Height;

            resultNode = new TextNode("", Theme.Current.Rounds.Text.XNA);
            resultNode.RelativeBounds = new RectangleF(pilotNode.RelativeBounds.Right, y, 1 - pilotNode.RelativeBounds.Right, height);
            resultNode.Alignment = RectangleAlignment.BottomLeft;
            resultNode.Scale(0.9f, 1);

            AddChild(resultNode);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Pressed && Pilot != null)
            {
                GetLayer<DragLayer>()?.RegisterDrag(this, mouseInputEvent);
                return true;
            }
            return base.OnMouseInput(mouseInputEvent);
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            PilotRaceInfoNode prin = node as PilotRaceInfoNode;
            IPilot ipilotnode = node as IPilot;

            if (ipilotnode == null)
                return false;

            if (EventRaceNode.Race.Ended && (ipilotnode != null || prin != null))
            {
                if (!EventRaceNode.PilotRaceInfoNodes.Contains(node))
                {
                    GetLayer<PopupLayer>().PopupMessage("Can't add Pilot to a race that has finished");
                }
                return false;
            }

            Channel channel = Channel;
            if (ipilotnode.Channel != null && channel != null)
            {
                Band desiredBand = ipilotnode.Channel.Band;
                if (channel.Band != desiredBand)
                {
                    Channel maybe = SharedChannels.FirstOrDefault(r => r.Band == desiredBand);
                    if (maybe != null)
                    {
                        channel = maybe;
                    }
                }
            }

            using (IDatabase db = DatabaseFactory.Open(EventRaceNode.EventManager.EventId))
            {
                if (prin != null)
                {
                    Race oldRace = prin.EventRaceNode.Race;

                    if (oldRace == null || oldRace.RoundNumber != EventRaceNode.Race.RoundNumber)
                    {
                        EventRaceNode.Race.SetPilot(db, channel, prin.Pilot);
                        EventRaceNode.SyncSheetChange();
                        prin.EventRaceNode.SyncSheetChange();
                        EventRaceNode.Refresh();
                        return true;
                    }

                    if (!oldRace.Ended)
                    {
                        EventRaceNode.Race.SwapPilots(db, prin.Pilot, channel, oldRace);
                        EventRaceNode.SyncSheetChange();
                        prin.EventRaceNode.SyncSheetChange();
                        EventRaceNode.Refresh();
                        return true;
                    }
                }
                if (ipilotnode != null)
                {
                    if (EventRaceNode.Race.HasPilot(ipilotnode.Pilot))
                    {
                        EventRaceNode.Race.SwapPilots(db, ipilotnode.Pilot, channel, EventRaceNode.Race);
                        EventRaceNode.SyncSheetChange();
                        EventRaceNode.Refresh();
                    }
                    else
                    {
                        EventRaceNode.Race.ClearChannel(db, channel);
                        EventRaceNode.Race.SetPilot(db, channel, ipilotnode.Pilot);
                        EventRaceNode.SyncSheetChange();
                        EventRaceNode.Refresh();
                    }

                    return true;
                }
            }


            return base.OnDrop(finalInputEvent, node);
        }
    }
}
