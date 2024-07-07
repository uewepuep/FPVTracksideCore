using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tools;

namespace UI.Nodes
{
    public delegate void PilotClickDelegate(MouseInputEvent mie, Pilot p);

    public class PilotListNode : Node, IUpdateableNode
    {
        private List<Pilot> pilots;

        private ListNode<PilotChannelNode> listNode;

        public event PilotClickDelegate OnTakePhoto;
        public event PilotClickDelegate OnPilotClick;
        public event PilotClickDelegate OnPilotChannelClick;

        public EventManager eventManager;

        private Node instructionNode;

        public PilotListNode(EventManager eventManager)
        {
            this.eventManager = eventManager;

            pilots = new List<Pilot>();

            listNode = new ListNode<PilotChannelNode>(Theme.Current.ScrollBar.XNA);
            listNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            listNode.ItemHeight = 30;
            listNode.NodeName = "PilotListNode";

            instructionNode = new Node();
            instructionNode.RelativeBounds = new RectangleF(0.05f, 0.4f, 0.8f, 0.05f);
                
            TextNode text = new TextNode("Right click here on the side panel\n to add pilots to the event.", Theme.Current.LeftPilotList.Text.XNA);
            text.Alignment = RectangleAlignment.Center;
            instructionNode.AddChild(text);

            AddChild(listNode);

            AddChild(instructionNode);

            Add(eventManager.Event.PilotChannels);

            eventManager.OnPilotChangedChannels += UpdatePilotChannel;
            eventManager.OnPilotRefresh += RebuildList;

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
            foreach (var pl in listNode.Children.OfType<PilotChannelNode>())
            {
                if (pl.Pilot == pc.Pilot)
                {
                    pl.SetPilotChannel(pc);
                }
            }
            listNode.RequestRedraw();
        }

        private void AddPilot()
        {
            GetLayer<PopupLayer>().Popup(new AddPilotNode(eventManager));
        }

        public void Add(IEnumerable<PilotChannel> pcs)
        {
            foreach (PilotChannel p in pcs.ToArray())
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
            pn.UpdateProfileIcon();
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

            listNode.AddChild(pn);
            listNode.SetOrder<PilotChannelNode, string>(pa => pa.Pilot.Name);

            instructionNode.Visible = false;
            listNode.RequestLayout();
        }

        public void ClearList()
        {
            pilots.Clear();
            listNode.ClearDisposeChildren();

            instructionNode.Visible = true;
            listNode.RequestLayout();
        }

        private bool rebuildList;
        public void RebuildList()
        {
            rebuildList = true;
        }

        private PilotChannelNode GetPilotChannelNodeFromMouseInputEvent(MouseInputEvent mouseInputEvent)
        {
            foreach (var pcn in listNode.ChildrenOfType)
            {
                if (pcn.BoundsF.Contains(mouseInputEvent.Position))
                {
                    return pcn;
                }
            }
            return null;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            MouseInputEvent translated = listNode.Translate(mouseInputEvent);

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

            if (base.OnMouseInput(mouseInputEvent))
                return true;

            return false;
        }

        private void MakeMenu(MouseInputEvent mouseInputEvent, Pilot pilot)
        {
            MouseMenu mm = new MouseMenu(this);
            mm.AddItem("Add Pilot", () =>
            {
                AddPilot();
            });

            bool anyPilots = eventManager.Event.Pilots.Where(p => p != null).Any();
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

                    mm.AddItem("Take photo", () =>
                    {
                        OnTakePhoto?.Invoke(mouseInputEvent, pilot);
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
                    IEnumerable<string> pilotNames = pilots.Select(p => p.Name).OrderBy(r => r);
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

            MouseMenu importMenu = mm.AddSubmenu("Import"); 

            Event[] others = eventManager.GetOtherEvents();
            if (others.Any())
            {
                MouseMenu otherEvents = importMenu.AddSubmenu("Import from another event");
                foreach (Event e in others)
                {
                    if (e.Enabled && e.PilotCount > 0)
                    {
                        otherEvents.AddItem(e.Name, () => { ImportFromOtherEvent(e); });
                    }
                }
            }

            importMenu.AddItem("Import from profile picture filenames", () =>
            {
                List<string> names = new List<string>();
                foreach (FileInfo fileInfo in eventManager.GetPilotProfileMedia())
                {
                    string name = fileInfo.Name.Replace(fileInfo.Extension, "");
                    if (!names.Contains(name))
                    {
                        names.Add(name);
                    }
                }

                IEnumerable<PilotChannel> newPilots = ImportFromNames(names);
                EditPilotChannels(newPilots, null);
                RebuildList();
            });


#if DEBUG
            importMenu.AddItem("Import Debug Pilots", () =>
            {
                string[] pilotNames = new string[]
                {
                   "Alfa",
                   "Bravo",
                   "Charlie",
                   "Delta",
                   "Echo",
                   "Foxtrot",
                   "Golf",
                   "Hotel",
                   "India",
                   "Juliett",
                   "Kilo",
                   "Lima",
                   "Mike",
                   "November",
                   "Oscar",
                   "Papa",
                   "Quebec",
                   "Romeo",
                   "Sierra",
                   "Tango",
                   "Uniform",
                   "Victor",
                   "Whiskey",
                   "X-ray",
                   "Yankee",
                   "Zulu",
                };
                IEnumerable<PilotChannel> newPilots = ImportFromNames(pilotNames);
                EditPilotChannels(newPilots, null);
                RebuildList();
            });
#endif


            if (anyPilots)
            {
                mm.AddBlank();

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
            foreach (Pilot p in eventManager.GetOtherEventPilots(evente))
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

        public void Update(GameTime gameTime)
        {
            if (rebuildList)
            {
                ClearList();
                Add(eventManager.Event.PilotChannels);
                rebuildList = false;
            }
        }
    }
}
