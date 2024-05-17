using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Timing.ImmersionRC;
using Timing.RotorHazard;
using Tools;

namespace UI.Nodes
{
    class TimingSystemEditor : ObjectEditorNode<TimingSystemSettings>
    {
        public TextButtonNode ScanButton { get; private set; }


        public IEnumerable<string> Hostnames
        {
            get
            {
                if (Objects == null)
                    yield break;

                foreach (TimingSystemSettings timingSystemSetting in Objects)
                {
                    if (timingSystemSetting is RotorHazardSettings)
                    {
                        yield return ((RotorHazardSettings)timingSystemSetting).HostName;
                    }
                    if (timingSystemSetting is LapRFSettingsEthernet)
                    {
                        yield return ((LapRFSettingsEthernet)timingSystemSetting).HostName;
                    }
                }
            }
        }

        public TimingSystemEditor(IEnumerable<TimingSystemSettings> toEdit)
            : base(toEdit, true, true)
        {
            Text = "Timing Settings";

            ScanButton = new TextButtonNode("Scan Network", ButtonBackground, ButtonHover, TextColor);
            buttonContainer.AddChild(ScanButton);

            Node[] buttons = new Node[] { ScanButton, addButton, removeButton, cancelButton, okButton };
            buttonContainer.SetOrder(buttons);

            ScanButton.OnClick += ScanButton_OnClick;

            AlignVisibleButtons();
        }

        private void ScanButton_OnClick(MouseInputEvent mie)
        {
            LoadingLayer ll = CompositorLayer.LayerStack.GetLayer<LoadingLayer>();
            if (ll != null)
            {
                ll.WorkQueue.Enqueue("Scanning Network", () =>
                {
                    SubnetScanner ss = new SubnetScanner();
                    ss.Exceptions = Hostnames.ToArray();

                    int lapRFPort = (new LapRFSettingsEthernet()).Port;
                    int rhPort = (new RotorHazardSettings()).Port;


                    MouseMenu mouseMenu = new MouseMenu(ScanButton);
                    foreach(SubnetScanner.OpenPortsStruct openPort in ss.AliveWithOpenPorts(lapRFPort, rhPort))
                    {
                        foreach (int port in openPort.Ports)
                        {
                            IPAddress copy = openPort.Address;

                            if (port == lapRFPort)
                            {
                                mouseMenu.AddItem("Add LapRF 8way - " + copy, () => 
                                {
                                    var laprf = new LapRFSettingsEthernet();
                                    laprf.HostName = copy.ToString();
                                    AddNew(laprf);
                                });
                            }

                            if (port == rhPort)
                            {
                                mouseMenu.AddItem("Add RotorHazard - " + copy, () => 
                                {
                                    var rotorhazard = new RotorHazardSettings();
                                    rotorhazard.HostName = copy.ToString();
                                    AddNew(rotorhazard);
                                });
                            }
                        }
                    }

                    mouseMenu.TopToBottom = false;
                    mouseMenu.Show(ScanButton);

                });
            }
        }

        protected override void AddOnClick(MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            mouseMenu.AddItem("LapRF 8-way", () => { AddNew(new Timing.ImmersionRC.LapRFSettingsEthernet()); });
            mouseMenu.AddItem("LapRF Puck", () => { AddNew(new Timing.ImmersionRC.LapRFSettingsUSB()); });
            mouseMenu.AddItem("RotorHazard 4.0+", () => { AddNew(new Timing.RotorHazard.RotorHazardSettings()); });
            //mouseMenu.AddItem("Video Color (Alpha)", () => { AddNew(new VideoTimingSettings()); });
            mouseMenu.AddItem("Dummy", () => { AddNew(new DummySettings()); });

            mouseMenu.Show(addButton);
        }

        protected override IEnumerable<PropertyInfo> GetPropertyInfos(TimingSystemSettings obj)
        {
            // Just a little hack to make all the "receiver" setting appear last. 
            List<PropertyInfo> lapRFBaseSettings = new List<PropertyInfo>();

            foreach (var pi in base.GetPropertyInfos(obj))
            {
                if (pi.ReflectedType == typeof(Timing.ImmersionRC.LapRFSettings))
                {
                    lapRFBaseSettings.Add(pi);
                }
                else
                {
                    yield return pi;
                }
            }

            foreach (var pi in lapRFBaseSettings)
            {
                yield return pi;
            }
        }

        protected override string ItemToString(TimingSystemSettings item)
        {
            string extraInfo = "";
            
            if (Objects.Count > 1)
            {
                if (item.Role == TimingSystemRole.Split)
                {
                    extraInfo = " (Split " + (Objects.Where(r => r.Role == TimingSystemRole.Split).ToList().IndexOf(item) + 1) + ")";
                }
                else
                {
                    extraInfo = " (Primary)";
                }
            }

            return base.ItemToString(item) + extraInfo;
        }

        protected override PropertyNode<TimingSystemSettings> CreatePropertyNode(TimingSystemSettings obj, PropertyInfo pi)
        {
            if (pi.Name == "ComPort")
            {
                return new ComPortPropertyNode<TimingSystemSettings>(obj, pi, ButtonBackground, TextColor, ButtonHover);
            }

            return base.CreatePropertyNode(obj, pi);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            return base.OnMouseInput(mouseInputEvent);
        }

        
        protected override void AddNew(TimingSystemSettings t)
        {
            if (Objects.Any())
            {
                t.Role = TimingSystemRole.Split;
            }
            base.AddNew(t);
        }

        protected override void SetSelected(TimingSystemSettings obj)
        {
            base.SetSelected(obj);
            CheckVisible();
        }

        private void CheckVisible()
        {
            bool multipleCategoryVisible = Objects.Count > 1;

            foreach (var propertyNode in PropertyNodes)
            {
                if (propertyNode == null)
                    continue;

                CategoryAttribute ca = propertyNode.PropertyInfo.GetCustomAttribute<CategoryAttribute>();
                if (ca != null)
                {
                    if (ca.Category == "Multiple System Settings")
                    {
                        propertyNode.Visible = multipleCategoryVisible;
                    }
                }
            }
        }
    }
}
