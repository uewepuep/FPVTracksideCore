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
using Timing;
using Timing.Velocidrone;
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

            SimpleEvent[] others = eventManager.GetOtherEvents();
            if (others.Any())
            {
                MouseMenu otherEvents = importMenu.AddSubmenu("Import from another event");
                foreach (SimpleEvent e in others)
                {
                    if (e.PilotsRegistered > 0)
                    {
                        otherEvents.AddItem(e.Name, () => { ImportFromOtherEvent(e); });
                    }
                }
            }

            importMenu.AddItem("Import from profile picture filenames", () =>
            {
                List<string> names = new List<string>();
                foreach (FileInfo fileInfo in eventManager.ProfilePictures.GetPilotProfileMedia())
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

            importMenu.AddItem("Import from Velocidrone", () =>
            {
                ImportFromVelocidrone(mouseInputEvent);
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

        private void ImportFromOtherEvent(SimpleEvent evente)
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

        private void ImportFromVelocidrone(MouseInputEvent mouseInputEvent)
        {
            var vdTiming = eventManager.RaceManager.TimingSystemManager.TimingSystems
                .OfType<VelocidroneTimingSystem>()
                .FirstOrDefault();

            if (vdTiming != null && vdTiming.Connected)
            {
                ImportFromVelocidroneUsingConnection(vdTiming);
                return;
            }

            string defaultAddress = GetVelocidroneDefaultAddress();
            var popup = new TextPopupNode("Import from Velocidrone", "Address (e.g. 192.168.1.100)", defaultAddress);
            popup.OnOK += (address) =>
            {
                ParseAddress(address, out string host, out int port);
                ImportFromVelocidroneUsingFetcher(host, port);
            };
            GetLayer<PopupLayer>()?.Popup(popup);
        }

        private void ImportFromVelocidroneUsingConnection(VelocidroneTimingSystem vdTiming)
        {
            var loadingLayer = GetLayer<LoadingLayer>();
            if (loadingLayer == null)
            {
                GetLayer<PopupLayer>()?.PopupMessage("Unable to show loading");
                return;
            }
            loadingLayer.WorkQueue.Enqueue("Importing from Velocidrone", () =>
            {
                var pilotInfos = vdTiming.RequestPilotList();
                var listNode = this;
                listNode.PlatformTools?.Invoke(() =>
                {
                    if (pilotInfos == null || pilotInfos.Count == 0)
                    {
                        listNode.GetLayer<PopupLayer>()?.PopupMessage("No pilots received. Ensure: 1) Velocidrone is in a multiplayer lobby, 2) You are race manager (host). Check the Timing log for received message keys.");
                        return;
                    }
                    var newPilots = listNode.ImportFromVelocidronePilots(pilotInfos);
                    listNode.EditPilotChannels(newPilots, null);
                    listNode.RebuildList();
                    listNode.GetLayer<PopupLayer>()?.PopupMessage($"Imported {newPilots.Count()} pilots from Velocidrone.");
                });
            });
        }

        private void ImportFromVelocidroneUsingFetcher(string host, int port)
        {
            var loadingLayer = GetLayer<LoadingLayer>();
            if (loadingLayer == null)
            {
                GetLayer<PopupLayer>()?.PopupMessage("Unable to show loading");
                return;
            }
            loadingLayer.WorkQueue.Enqueue("Importing from Velocidrone", () =>
            {
                var pilotInfos = VelocidronePilotFetcher.FetchPilots(host, port);
                var listNode = this;
                listNode.PlatformTools?.Invoke(() =>
                {
                    if (pilotInfos == null || pilotInfos.Count == 0)
                    {
                        listNode.GetLayer<PopupLayer>()?.PopupMessage("Could not connect or no pilots received. If Velocidrone timing is connected, it will be used automatically. Otherwise ensure: 1) Velocidrone is running, 2) Websocket enabled, 3) Correct IP (use 192.168.x.x not 127.0.0.1).");
                        return;
                    }
                    var newPilots = listNode.ImportFromVelocidronePilots(pilotInfos);
                    listNode.EditPilotChannels(newPilots, null);
                    listNode.RebuildList();
                    listNode.GetLayer<PopupLayer>()?.PopupMessage($"Imported {newPilots.Count()} pilots from Velocidrone.");
                });
            });
        }

        private static void ParseAddress(string address, out string host, out int port)
        {
            host = "localhost";
            port = VelocidroneProtocol.DefaultPort;
            if (string.IsNullOrWhiteSpace(address)) return;
            var parts = address.Trim().Split(new[] { ':', ',' }, StringSplitOptions.RemoveEmptyEntries);
            host = parts[0].Trim();
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int p) && p > 0)
                port = p;
        }

        private string GetVelocidroneDefaultAddress()
        {
            var settings = TimingSystemSettings.Read(eventManager.Profile);
            var vd = settings?.OfType<VelocidroneSettings>().FirstOrDefault();
            return vd != null ? vd.HostName + ":" + vd.Port : "";
        }

        private IEnumerable<PilotChannel> ImportFromVelocidronePilots(IEnumerable<VelocidronePilotFetcher.PilotInfo> pilotInfos)
        {
            var newPilots = new List<PilotChannel>();
            int channelIndex = 0;

            using (var db = DatabaseFactory.Open(eventManager.EventId))
            {
                foreach (var info in pilotInfos)
                {
                    if (string.IsNullOrEmpty(info.Name)) continue;

                    Pilot p = eventManager.GetCreatePilot(info.Name);
                    p.VelocidroneUID = info.Uid;
                    db.Upsert(p);

                    if (newPilots.Any(pc => pc.Pilot.Name == p.Name)) continue;

                    Channel channel;
                    var existingPc = eventManager.GetPilotChannel(p);
                    if (existingPc != null)
                    {
                        channel = existingPc.Channel;
                    }
                    else
                    {
                        var channelGroup = eventManager.Channels.GetChannelGroup(channelIndex);
                        channel = channelGroup?.FirstOrDefault();
                        channelIndex = (channelIndex + 1) % eventManager.GetMaxPilotsPerRace();
                    }

                    if (channel != null)
                    {
                        var pc = new PilotChannel(p, channel);
                        newPilots.Add(pc);
                        if (existingPc == null)
                        {
                            eventManager.AddPilot(pc);
                        }
                    }
                }
            }

            return newPilots;
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
