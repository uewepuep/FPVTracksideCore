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
using Timing.ELRS;
using Timing.ImmersionRC;
using Timing.RotorHazard;
using Timing.Velocidrone;
using Tools;

namespace UI.Nodes
{
    class TimingSystemEditor : ObjectEditorNode<TimingSystemSettings>
    {
        public TextButtonNode ScanButton { get; private set; }
        public TextButtonNode TestConnectionButton { get; private set; }


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
                    if (timingSystemSetting is VelocidroneSettings)
                    {
                        yield return ((VelocidroneSettings)timingSystemSetting).HostName;
                    }
                }
            }
        }

        public TimingSystemEditor(IEnumerable<TimingSystemSettings> toEdit)
            : base(toEdit, true, true)
        {
            Text = "Timing Settings";

            ScanButton = new TextButtonNode("Scan", ButtonBackground, ButtonHover, TextColor);
            buttonContainer.AddChild(ScanButton);

            TestConnectionButton = new TextButtonNode("Test Connection", ButtonBackground, ButtonHover, TextColor);
            buttonContainer.AddChild(TestConnectionButton);

            Node[] buttons = new Node[] { ScanButton, TestConnectionButton, addButton, removeButton, cancelButton, okButton };
            buttonContainer.SetOrder(buttons);

            ScanButton.OnClick += ScanButton_OnClick;
            TestConnectionButton.OnClick += TestConnectionButton_OnClick;

            AlignVisibleButtons();
        }

        private void TestConnectionButton_OnClick(MouseInputEvent mie)
        {
            TimingSystemSettings settings = Selected;
            if (settings == null)
                return;

            ITimingSystem timingSystem = CreateTestInstance(settings);
            if (timingSystem == null)
            {
                GetLayer<PopupLayer>()?.PopupMessage("Test Connection isn't supported for this timing system type.");
                return;
            }

            LoadingLayer ll = GetLayer<LoadingLayer>();
            if (ll == null)
                return;

            ll.WorkQueue.Enqueue("Testing Connection", () =>
            {
                try
                {
                    bool connected;
                    if (settings is DummySettings dummySettings)
                    {
                        // Dummy has no real connection to test, so simulate a result based on the configured failure rate.
                        connected = Random.Shared.NextDouble() * 100 >= dummySettings.TestConnectionFailureRatePercent;
                    }
                    else
                    {
                        connected = timingSystem.Connect();
                    }
                    timingSystem.Disconnect();

                    if (connected)
                    {
                        GetLayer<PopupLayer>()?.PopupMessage("Connected successfully to " + settings.ToString() + ".");
                    }
                    else
                    {
                        GetLayer<PopupLayer>()?.PopupMessage("Failed to connect to " + settings.ToString() + ".");
                    }
                }
                catch (Exception ex)
                {
                    GetLayer<PopupLayer>()?.PopupError("Failed to connect to " + settings.ToString() + ".", ex);
                }
                finally
                {
                    timingSystem.Dispose();
                }
            });
        }

        // Only settings types with a network connection worth testing (or, for Dummy, a simulated
        // one) are offered here. USB/serial and camera-based systems (ELRS, Aruco, LapRF Puck) are
        // excluded since triggering them from a settings-screen click could grab onto hardware
        // that's already in use elsewhere.
        private static bool IsTestable(TimingSystemSettings settings)
        {
            return settings is RotorHazardSettings
                || settings is LapRFSettingsEthernet
                || settings is VelocidroneSettings
                || settings is Timing.Chorus.ChorusSettings
                || settings is DummySettings;
        }

        private static ITimingSystem CreateTestInstance(TimingSystemSettings settings)
        {
            if (!IsTestable(settings))
                return null;

            return TimingSystemManager.CreateTimingSystem(settings);
        }

        private void ScanButton_OnClick(MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(ScanButton);
            mouseMenu.AddItem("Scan Network", ScanNetwork);
            mouseMenu.AddItem("Scan Serial", ScanSerial);
            mouseMenu.TopToBottom = false;
            mouseMenu.Show(ScanButton);
        }

        private void ScanNetwork()
        {
            LoadingLayer ll = GetLayer<LoadingLayer>();
            if (ll != null)
            {
                ll.WorkQueue.Enqueue("Scanning Network", () =>
                {
                    SubnetScanner ss = new SubnetScanner();
                    ss.Exceptions = Hostnames.ToArray();

                    int lapRFPort = (new LapRFSettingsEthernet()).Port;
                    int rhPort = (new RotorHazardSettings()).Port;
                    int vdPort = (new VelocidroneSettings()).Port;

                    MouseMenu mouseMenu = new MouseMenu(ScanButton);
                    foreach(SubnetScanner.OpenPortsStruct openPort in ss.AliveWithOpenPorts(lapRFPort, rhPort, vdPort))
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

                            if (port == vdPort)
                            {
                                mouseMenu.AddItem("Add Velocidrone - " + copy, () =>
                                {
                                    var velocidrone = new VelocidroneSettings();
                                    velocidrone.HostName = copy.ToString();
                                    AddNew(velocidrone);
                                });
                            }
                        }
                    }

                    mouseMenu.TopToBottom = false;
                    mouseMenu.Show(ScanButton);
                });
            }
        }

        private void ScanSerial()
        {
            LoadingLayer ll = GetLayer<LoadingLayer>();
            if (ll != null)
            {
                ll.WorkQueue.Enqueue("Scanning Serial", () =>
                {
                    MouseMenu mouseMenu = new MouseMenu(ScanButton);
                    bool foundAny = false;

                    string elrsPort = VRXCProtocol.DetectPort();
                    if (!string.IsNullOrEmpty(elrsPort))
                    {
                        foundAny = true;
                        mouseMenu.AddItem("Add ELRS Backpack - " + elrsPort, () =>
                        {
                            var elrs = new ELRSSettings();
                            elrs.ComPort = elrsPort;
                            AddNew(elrs);
                        });
                    }

                    string lapRFPort = LapRFTimingUSB.DetectPort();
                    if (!string.IsNullOrEmpty(lapRFPort))
                    {
                        foundAny = true;
                        mouseMenu.AddItem("Add LapRF Puck - " + lapRFPort, () =>
                        {
                            var laprf = new LapRFSettingsUSB();
                            laprf.ComPort = lapRFPort;
                            AddNew(laprf);
                        });
                    }

                    if (!foundAny)
                    {
                        mouseMenu.AddItem("No serial timing systems found", () => { });
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
            mouseMenu.AddItem("Velocidrone", () => { AddNew(new VelocidroneSettings()); });
            mouseMenu.AddItem("Chorus32", () => { AddNew(new Timing.Chorus.ChorusSettings()); });
            mouseMenu.AddItem("ELRS Backpack (Race Control)", () => { AddNew(new ELRSSettings()); });
            
            if (Timing.Aruco.ArucoTimingSystem.IsNativeAvailable())
                mouseMenu.AddItem("ArUco (Video Marker)", () => { AddNew(new Timing.Aruco.ArucoTimingSettings()); });
            else
                mouseMenu.AddDisabledItem("ArUco (Video Marker) [needs OpenCV installed]");

            mouseMenu.AddItem("Dummy", () => { AddNew(new DummySettings()); });

            mouseMenu.Show(addButton);
        }

        protected override IEnumerable<PropertyInfo> GetPropertyInfos(TimingSystemSettings obj)
        {
            // Just a little hack to make all the "receiver" setting appear last.
            List<PropertyInfo> lapRFBaseSettings = new List<PropertyInfo>();

            bool isArucoSplit =
                obj is Timing.Aruco.ArucoTimingSettings &&
                obj.Role == TimingSystemRole.Split;

            // Find the reference ArUco instance — Primary if present, otherwise the lowest-index
            // Split. The reference keeps ALL its settings editable; every other Split only
            // exposes MarkerIds because it inherits shared parameters from the reference.
            var arucoInstances = Objects.OfType<Timing.Aruco.ArucoTimingSettings>().ToList();
            var arucoPrimary = arucoInstances.FirstOrDefault(x => x.Role == TimingSystemRole.Primary);
            var arucoReference = arucoPrimary
                ?? arucoInstances.FirstOrDefault(x => x.Role == TimingSystemRole.Split);
            bool isArucoReference = ReferenceEquals(obj, arucoReference);

            // Role is locked to Split only when a real Primary already exists elsewhere.
            bool lockRoleForArucoSplit = isArucoSplit && arucoPrimary != null;

            foreach (var pi in base.GetPropertyInfos(obj))
            {
                if (obj is ELRSSettings && pi.Name == "Role")
                    continue;

                if (lockRoleForArucoSplit && pi.Name == "Role")
                    continue;

                // Non-reference Split ArUco: hide all ArUco-specific properties except MarkerIds.
                // Thresholds/detector parameters are inherited from the reference at runtime.
                if (isArucoSplit && !isArucoReference &&
                    pi.DeclaringType == typeof(Timing.Aruco.ArucoTimingSettings) &&
                    pi.Name != "MarkerIds")
                    continue;

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

            int lapTimingSystemCount = Objects.Count(obj => !(obj is ELRSSettings));
            if (!(item is ELRSSettings) && lapTimingSystemCount > 1)
            {
                if (item.Role == TimingSystemRole.Split)
                {
                    extraInfo = " (Split " + (Objects.Where(r => !(r is ELRSSettings) && r.Role == TimingSystemRole.Split).ToList().IndexOf(item) + 1) + ")";
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
            if (t is ELRSSettings)
            {
                t.Role = TimingSystemRole.Split;
            }
            else if (Objects.Any(obj => !(obj is ELRSSettings)))
            {
                t.Role = TimingSystemRole.Split;
            }
            else
            {
                t.Role = TimingSystemRole.Primary;
            }
            base.AddNew(t);
        }

        protected override void DoSetSelected(TimingSystemSettings obj)
        {
            base.DoSetSelected(obj);
            CheckVisible();
        }

        private void CheckVisible()
        {
            bool multipleCategoryVisible = Objects.Count(obj => !(obj is ELRSSettings)) > 1;

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
