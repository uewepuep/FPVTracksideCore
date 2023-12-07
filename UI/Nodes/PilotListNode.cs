using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Nodes
{
    public delegate void PilotClickDelegate(MouseInputEvent mie, Pilot p);

    public class PilotListNode : Node
    {
        private List<Pilot> pilots;

        private ListNode<PilotChannelNode> pilotListNode;

        public event PilotClickDelegate OnPilotClick;
        public event PilotClickDelegate OnPilotChannelClick;

        public EventManager eventManager;

        private Node instructionNode;

        private TextButtonNode heading;

        public PilotListNode(EventManager eventManager)
        {
            this.eventManager = eventManager;

            pilots = new List<Pilot>();

            heading = new TextButtonNode("", Theme.Current.Panel.XNA, Color.Transparent, Theme.Current.LeftPilotList.PilotCount.XNA);
            heading.RelativeBounds = new RectangleF(0, 0, 1, 0.035f);
            heading.Enabled = false;
            AddChild(heading);

            pilotListNode = new ListNode<PilotChannelNode>(Theme.Current.ScrollBar.XNA);
            pilotListNode.RelativeBounds = new RectangleF(0, heading.RelativeBounds.Bottom, 1, 1 - heading.RelativeBounds.Bottom);
            pilotListNode.ItemHeight = 30;
            pilotListNode.NodeName = "PilotListNode";

            instructionNode = new Node();
            instructionNode.RelativeBounds = new RectangleF(0.05f, 0.4f, 0.8f, 0.05f);
                
            TextNode text = new TextNode("Right click here on the side panel\n to add pilots to the event.", Theme.Current.LeftPilotList.Text.XNA);
            text.Alignment = RectangleAlignment.Center;
            instructionNode.AddChild(text);

            AddChild(pilotListNode);

            AddChild(instructionNode);

            Add(eventManager.Event.PilotChannels);

            eventManager.OnPilotChangedChannels += UpdatePilotChannel;
            eventManager.OnPilotRefresh += RebuildList;

            UpdateHeading();

            RequestLayout();
        }


        public override void Dispose()
        {
            eventManager.OnPilotChangedChannels -= UpdatePilotChannel;
            eventManager.OnPilotRefresh -= RebuildList;

            base.Dispose();
        }

        public void UpdatePilotChannel(Pilot pilot)
        {
            UpdatePilotChannel(eventManager.GetPilotChannel(pilot));
        }

        public void UpdatePilotChannel(PilotChannel pc)
        {
            foreach (var pl in pilotListNode.Children.OfType<PilotChannelNode>())
            {
                if (pl.Pilot == pc.Pilot)
                {
                    pl.SetPilotChannel(pc);
                }
            }
            pilotListNode.RequestRedraw();
        }

        private void AddPilot()
        {
            GetLayer<PopupLayer>().Popup(new AddPilotNode(eventManager));
        }

        public void Add(IEnumerable<PilotChannel> pcs)
        {
            foreach (PilotChannel p in pcs)
            {
                Add(p);
            }
        }

        public void Add(PilotChannel pc)
        {
            if (pc.Pilot == null || pc.Channel == null)
                return;

            if (pc.Pilot.PracticePilot)
                return;

            pilots.Add(pc.Pilot);

            PilotChannelNode pn = new PilotChannelNode(eventManager, Theme.Current.LeftPilotList.Foreground, Theme.Current.Hover.XNA, Theme.Current.LeftPilotList.Text.XNA, Theme.Current.LeftPilotList.Channel);
            pn.SetPilotChannel(pc);
            pn.OnPilotClick += (mie, ap) => 
            {
                if (mie.Button == MouseButtons.Left)
                {
                    OnPilotClick?.Invoke(mie, ap);
                }

                if (mie.Button == MouseButtons.Right)
                {
                    MakeMenu(mie, ap);
                }
            };
            pn.OnPilotChannelClick += (mie, ap) => { OnPilotChannelClick?.Invoke(mie, ap); };

            pilotListNode.AddChild(pn);
            pilotListNode.SetOrder<PilotChannelNode, string>(pa => pa.Pilot.Name);

            instructionNode.Visible = false;
            pilotListNode.RequestLayout();

            UpdateHeading();
        }

        private void UpdateHeading()
        {
            heading.Text = pilotListNode.ChildCount + " Pilots in event";
        }

        public void ClearList()
        {
            pilots.Clear();
            pilotListNode.ClearDisposeChildren();

            instructionNode.Visible = true;
            pilotListNode.RequestLayout();
        }

        public void RebuildList()
        {
            ClearList();
            Add(eventManager.Event.PilotChannels);
        }

        private PilotChannelNode GetPilotChannelNodeFromMouseInputEvent(MouseInputEvent mouseInputEvent)
        {

            foreach (var pcn in pilotListNode.ChildrenOfType)
            {
                if (pcn.Bounds.Contains(mouseInputEvent.Position))
                {
                    return pcn;
                }
            }
            return null;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (base.OnMouseInput(mouseInputEvent))
                return true;
            
            //MouseInputEvent translated = pilotListNode.Translate(mouseInputEvent);
            MouseInputEvent translated = mouseInputEvent;

            PilotChannelNode pcn = GetPilotChannelNodeFromMouseInputEvent(translated);
            if (translated.Button == MouseButtons.Right && translated.ButtonState == ButtonStates.Released)
            {
                Pilot pilot = null;
                if (pcn != null)
                    pilot = pcn.Pilot;

                MakeMenu(translated, pilot);
               
                return true;
            }

            if (pcn != null && translated.Button == MouseButtons.Left && translated.ButtonState == ButtonStates.Pressed)
            {
                GetLayer<DragLayer>()?.RegisterDrag(pcn, translated);
            }

            return false;
        }

        private void MakeMenu(MouseInputEvent mouseInputEvent, Pilot pilot)
        {
            MouseMenu mm = new MouseMenu(this);
            mm.AddItem("Add Pilot", () =>
            {
                AddPilot();
            });

            bool anyPilots = eventManager.Event.Pilots.Any();
            if (anyPilots)
            {
                if (pilot != null)
                {
                    mm.AddItem("Edit Pilot", () =>
                    {
                        EditPilots(eventManager.Event.Pilots, pilot);
                    });

                    mm.AddItem("Remove " + pilot.Name + " from event", () =>
                    {
                        eventManager.RemovePilot(pilot);
                    });
                }
                else
                {
                    mm.AddItem("Edit Pilots", () =>
                    {
                        EditPilots(eventManager.Event.Pilots, pilot);
                    });
                }

                mm.AddBlank();

                mm.AddItem("Copy Pilots", () =>
                {
                    IEnumerable<string> pilotNames = pilots.Select(p => p.Name);
                    PlatformTools.Clipboard.SetLines(pilotNames);
                });
            }

            mm.AddItem("Paste Pilots", () =>
            {
                IEnumerable<PilotChannel> newPilots = ImportFromNames(PlatformTools.Clipboard.GetLines());
                EditPilotChannels(newPilots, null);
                RebuildList();
            });


            if (eventManager.Event.RemovedPilots.Any())
            {
                mm.AddBlank();
                mm.AddSubmenu("Restore Removed Pilots", (c) => { eventManager.AddPilot(c); RebuildList(); }, eventManager.Event.RemovedPilots.ToArray());
            }

            Event[] others = eventManager.GetOtherEvents();
            if (others.Any())
            {
                mm.AddBlank();
                MouseMenu otherEvents = mm.AddSubmenu("Import from another event");
                foreach (Event e in others)
                {
                    if (e.Enabled && e.PilotCount > 0)
                    {
                        otherEvents.AddItem(e.Name, () => { ImportFromOtherEvent(e); });
                    }
                }
            }

            if (anyPilots)
            {
                mm.AddItem("Redistribute Channels", () =>
                {
                    eventManager.RedistrubuteChannels();
                    RebuildList();
                });


                mm.AddItemConfirm("Remove All Pilots", () =>
                {
                    eventManager.RemovePilots();
                    RebuildList();
                });
            }

            mm.Show(mouseInputEvent);
        }

        private IEnumerable<PilotChannel> ImportFromNames(IEnumerable<string> pilotNames)
        {
            List<PilotChannel> newPilots = new List<PilotChannel>();
            int channelIndex = 0;

            foreach (string untrimmed in pilotNames)
            {
                string pilotname = untrimmed.Trim();
                if (pilotname.Length > 0)
                {
                    // if its not in the list.
                    if (pilots.FirstOrDefault(p => p.Name.ToLower() == pilotname.ToLower()) == null)
                    {
                        Pilot p = eventManager.GetCreatePilot(pilotname);
                        if (newPilots.FirstOrDefault(p2 => p.Name == p2.Pilot.Name) == null)
                        {
                            var cs = eventManager.Channels.GetChannelGroup(channelIndex);
                            if (cs != null) 
                            {
                                Channel c = cs.FirstOrDefault();
                                if (c != null)
                                {
                                    newPilots.Add(new PilotChannel(p, c));
                                }
                            }
                            else
                            {

                            }
                        }
                    }
                }

                //Increment channels by 1
                channelIndex = (channelIndex + 1) % eventManager.GetMaxPilotsPerRace();
            }
            return newPilots;
        }

        private void ImportFromOtherEvent(Event evente)
        {
            List<Pilot> newPilots = new List<Pilot>();
            foreach (Pilot p in evente.Pilots)
            {
                Pilot existing = pilots.FirstOrDefault(ep => ep.Name == p.Name);
                if (existing != null)
                {
                    continue;
                }

                newPilots.Add(p);
            }

            EditPilots(newPilots, null);
            RebuildList();
        }

        private void EditPilots(IEnumerable<Pilot> pilots, Pilot selected)
        {
            PilotEditor editor = new PilotEditor(eventManager, pilots.OrderBy(p => p.Name).ToArray());
            if (selected != null)
            {
                editor.Selected = selected;
            }

            GetLayer<PopupLayer>().Popup(editor);
            editor.OnOK += (a) =>
            {
                RebuildList();
            };
        }

        private void EditPilotChannels(IEnumerable<PilotChannel> pilotChannels, Pilot selected)
        {
            PilotEditor editor = new PilotEditor(eventManager, pilotChannels.OrderBy(p => p.Pilot.Name).ToArray());
            if (selected != null)
            {
                editor.Selected = selected;
            }

            GetLayer<PopupLayer>().Popup(editor);
            editor.OnOK += (a) =>
            {
                RebuildList();
            };
        }
    }
}
