using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using ExternalData;
using ImageServer;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Format;
using Sound;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timing;
using Tools;
using UI;
using UI.Video;

namespace UI.Nodes
{

    public class ObjectEditorNode<T> : BaseObjectEditorNode<T>
    {
        public ObjectEditorNode(T toEdit, bool addRemove = false, bool cancelButton = true, bool canReorder = true)
           : this(new T[] { toEdit }, addRemove, cancelButton, canReorder)
        {
        }

        public ObjectEditorNode(IEnumerable<T> toEdit, bool addRemove = false, bool cancelButton = true, bool canReorder = true)
            : this(canReorder)
        {
            SetObjects(toEdit, addRemove, cancelButton);
        }

        public ObjectEditorNode(bool canReorder = true)
            : base(Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA, Theme.Current.ScrollBar.XNA)
        {
            CanReOrder = canReorder;

            BorderPanelShadowNode background = new BorderPanelShadowNode(Theme.Current.Editor.Background, Theme.Current.Editor.Border.XNA);
            AddChild(background);
            SetBack(background);

            RemoveChild(root);

            background.Inner.AddChild(root);

            Scale(0.6f, 0.9f);
        }

        public void AlignVisibleButtons()
        {
            AlignHorizontally(0.05f, buttonContainer.Children.Where(b => b.Visible).ToArray());
        }
    }

    class PilotEditor : ObjectEditorNode<Pilot>
    {
        private EventManager eventManager;

        private List<PilotChannel> pilotChannels;

        public PilotEditor(EventManager eventManager, IEnumerable<PilotChannel> toEdit)
            : this(eventManager, toEdit.Select(pc => pc.Pilot))
        {
            pilotChannels.AddRange(toEdit);
        }

        public PilotEditor(EventManager eventManager, IEnumerable<Pilot> toEdit)
            : base(toEdit, false, true, false)
        {
            this.eventManager = eventManager;
            pilotChannels = new List<PilotChannel>();

            TextButtonNode speakButton = new TextButtonNode("Speak", ButtonBackground, ButtonHover, TextColor);
            speakButton.OnClick += (mie) =>
            {
                if (Selected != null)
                {
                    SoundManager.Instance.SpeakName(Selected);
                }
            };
            buttonContainer.AddChild(speakButton);

            AlignHorizontally(0.05f, addButton, null, speakButton, null, cancelButton, okButton);

            OnOK += PilotEditor_OnOK;
        }

        private void PilotEditor_OnOK(BaseObjectEditorNode<Pilot> obj)
        {
            IEnumerable<Pilot> editedPilots = this.Objects;
            using (Database db = new Database())
            {
                foreach (Pilot pa in editedPilots)
                {
                    db.Pilots.Upsert(pa);
                }
                eventManager.RefreshPilots(db);
            }

            foreach (Pilot p in editedPilots)
            {
                // Re match up the pilots to any provided channels
                PilotChannel pc = pilotChannels.FirstOrDefault(a => a.Pilot == p);

                // or any channels already added...
                if (pc == null)
                {
                    pc = eventManager.GetPilotChannel(p);
                }

                if (pc != null)
                {
                    eventManager.AddPilot(pc.Pilot, pc.Channel);
                }
                else
                {
                    eventManager.AddPilot(p);
                }
            }

        }
    }

    public class SoundEditor : ObjectEditorNode<Sound.Sound>
    {
        private Node variables;

        public override bool GroupItems { get { return true; } }

        public SoundEditor(SoundManager soundManager)
            : base(soundManager.Sounds, false, true, false)
        {
            TextButtonNode speakButton = new TextButtonNode("Play", ButtonBackground, ButtonHover, TextColor);
            speakButton.OnClick += (mie) =>
            {
                if (Selected != null)
                {
                    soundManager.StopSound();
                    soundManager.PlayTestSound(Selected.Key);
                }
            };
            buttonContainer.AddChild(speakButton);

            TextButtonNode reset = new TextButtonNode("Defaults", ButtonBackground, ButtonHover, TextColor);
            reset.OnClick += (mie) =>
            {
                GetLayer<PopupLayer>().PopupConfirmation("Reset all sounds to default?", () =>
                {
                    soundManager.Reset();
                    SetObjects(soundManager.Sounds);
                });
            };
            buttonContainer.AddChild(reset);

            AlignHorizontally(0.05f, reset, null, speakButton, null, cancelButton, okButton);

            OnOK += (e) =>
            {
                soundManager.Sounds = Objects;
            };
        }

        public override void SetObjects(IEnumerable<Sound.Sound> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            IEnumerable<Sound.Sound> ordered = toEdit.OrderBy(r => r.Category);

            base.SetObjects(ordered, addRemove, cancelButton);

            Dictionary<string, string> instructions = new Dictionary<string, string>()
            {
                { "{pilot}", "Pilot name"},
                { "{count}", "Lap/Sector Count"},
                { "{position}", "Race position. (first, second..)"},
                { "{time}", "Typically seconds but will be converted into minutes/hours if large enough"},
                { "{laptime}", "The time of this single lap."},
                { "{lapstime}", "The time of consecutive laps. "},
                { "{racetime}", "The time since the start of the race."},
                { "{type}", "Race Type (Race, Timetrial..)"},
                { "{round}", "Round number (1, 2..)"},
                { "{race}", "Race number (1, 2..)"},
                { "{bracket}", "Bracket (A, B, Winners, Losers..)"},
                { "{band}", "Channel band (Fatshark, Raceband..)"},
                { "{channel}", "Channel number  (1, 2..)"},
                { "{pilots}", "Pilots and their respective channels"},
                { "{s}", "Will be an 's' if {count} isn't '1'. Helps you say '2 laps', not '2 lap' and '1 lap' not '1 laps'."},

            };

            variables = new Node();
            right.AddChild(variables);

            TextNode s = new TextNode("Variables", TextColor);
            s.Style.Bold = true;
            variables.AddChild(s);

            foreach (var kvp in instructions)
            {
                TextNode variable = new TextNode(kvp.Key, TextColor);
                variable.Alignment = RectangleAlignment.CenterRight;
                variable.RelativeBounds = new RectangleF(0, 0, 0.1f, 1);
                TextNode instruction = new TextNode(kvp.Value, TextColor);
                instruction.Alignment = RectangleAlignment.CenterLeft;
                instruction.RelativeBounds = new RectangleF(0.15f, 0, 0.85f, 1);
                instruction.Style.Italic = true;

                Node row = new Node();
                row.AddChild(variable, instruction);

                variables.AddChild(row);
            }

            Node.AlignVertically(0.01f, variables.Children);

            float height = 0.4f;
            objectProperties.AddSize(0, -height);
            variables.RelativeBounds = new RectangleF(objectProperties.RelativeBounds.X, objectProperties.RelativeBounds.Bottom, objectProperties.RelativeBounds.Width, height);
            variables.Scale(0.9f, 0.9f);
        }

        protected override string ItemToGroupString(Sound.Sound item)
        {
            return item.Category.ToString();
        }

        protected override string ItemToString(Sound.Sound item)
        {
            return base.ItemToString(item).CamelCaseToHuman();
        }

        protected override PropertyNode<Sound.Sound> CreatePropertyNode(Sound.Sound obj, PropertyInfo pi)
        {
            if (pi.Name == "Filename")
            {
                return new FilenamePropertyNode<Sound.Sound>(obj, pi, ButtonBackground, ButtonHover, TextColor, "wav|*.wav");
            }

            return base.CreatePropertyNode(obj, pi);
        }
    }

    public class ExportColumnEditor : ObjectEditorNode<ExportColumn>
    {
        public ExportColumnEditor(EventManager eventManager)
            : base(ExportColumn.Read(), false, true, true)
        {
            OnOK += (e) =>
            {
                ExportColumn.Write(Objects.ToArray());
                if (eventManager != null)
                {
                    eventManager.ExportColumns = Objects.ToArray();
                }
            };
        }

        protected override string ItemToString(ExportColumn item)
        {
            return base.ItemToString(item).CamelCaseToHuman() + " (" + item.Enabled + ")";
        }
    }

    class ChannelEditor : ObjectEditorNode<Channel>
    {
        private TextCheckBoxNode check;

        public bool SaveDefault
        {
            get
            {
                if (check == null)
                {
                    return true;
                }
                return check.Value;
            }
        }

        private bool groupItems;
        public override bool GroupItems { get { return groupItems; } }

        public ChannelEditor(IEnumerable<Channel> toEdit, bool showCheckbox)
            : base(toEdit, true, true, false)
        {
            Text = "Channel Settings";
            if (showCheckbox)
            {
                check = new TextCheckBoxNode("Save as default for new Events", TextColor, false);
                check.RelativeBounds = new RectangleF(0, 0.97f, 0.8f, 0.025f);
                left.AddChild(check);
            }
        }

        protected override void AddOnClick(MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            Dictionary<string, Channel[]> allChannels = new Dictionary<string, Channel[]>();
            allChannels.Add("Fatshark", Channel.Fatshark);
            allChannels.Add("RaceBand", Channel.RaceBand);
            allChannels.Add("IMD6C", Channel.IMD6C);

            allChannels.Add("DJIFPVHD", Channel.DJIFPVHD);
            allChannels.Add("HDZero", Channel.HDZero);

            allChannels.Add("LowBand", Channel.LowBand);
            allChannels.Add("BoscamA", Channel.BoscamA);
            allChannels.Add("BoscamB", Channel.BoscamB);
            allChannels.Add("E", Channel.E);
            allChannels.Add("Diatone", Channel.Diatone);

            foreach (var kvp in allChannels)
            {
                string name = kvp.Key;
                Channel[] channels = kvp.Value;

                MouseMenu submenu = mouseMenu.AddSubmenu(name, AddNew, channels.ToArray());
                submenu.AddItem("All " + name, () => { AddAll(channels); });
            }

            mouseMenu.Show(addButton);
        }

        protected override void AddNew(Channel t)
        {
            if (!Objects.Contains(t))
            {
                base.AddNew(t);
            }

            UpdateGroupItems(Objects);
        }

        private void AddAll(IEnumerable<Channel> channels)
        {
            foreach (Channel c in channels)
            {
                AddNew(c);
            }
        }

        protected override void Remove(MouseInputEvent mie)
        {
            base.Remove(mie);
            UpdateGroupItems(Objects);
        }

        public override void SetObjects(IEnumerable<Channel> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            base.SetObjects(toEdit, addRemove, cancelButton);
            UpdateGroupItems(toEdit);
        }

        private void UpdateGroupItems(IEnumerable<Channel> channels)
        {
            groupItems = false;
            foreach (Channel c in channels)
            {
                if (c.GetInterferringChannels(channels).Where(ca => ca != c).Any())
                {
                    groupItems = true;
                }
            }
            RefreshList();
        }

        public override IEnumerable<Channel> Order(IEnumerable<Channel> ts)
        {
            if (ts == null)
                return null;

            return ts.OrderBy(c => c.Frequency).ThenBy(r => r.Band);
        }

        public int ChannelGroupIndex(Channel item)
        {
            var grouped = Objects.GetChannelGroups();

            int i = 0;
            foreach (var group in grouped)
            {
                i++;
                if (group.Contains(item))
                {
                    return i;
                }
            }

            return -1;
        }

        protected override string ItemToGroupString(Channel item)
        {
            return "Channel Group " + ChannelGroupIndex(item).ToString();
        }
    }

    class GeneralSettingsEditor : ObjectEditorNode<GeneralSettings>
    {
        public GeneralSettingsEditor(GeneralSettings toEdit)
            : base(toEdit, false, true, false)
        {
        }

        protected override PropertyNode<GeneralSettings> CreatePropertyNode(GeneralSettings obj, PropertyInfo pi)
        {
            if (pi.Name == "Voice")
            {
                VoicesPropertyNode listPropertyNode = new VoicesPropertyNode(obj, pi, TextColor, ButtonHover);
                return listPropertyNode;
            }

            if (pi.Name == "InverseResolutionScalePercent")
            {
                int[] scales = new int[] { 50, 75, 100, 125, 150, 200 };
                ListPropertyNode<GeneralSettings> listPropertyNode = new ListPropertyNode<GeneralSettings>(obj, pi, TextColor, ButtonHover, scales);
                return listPropertyNode;
            }

            if (pi.Name == "NotificationSerialPort")
            {
                return new ComPortPropertyNode<GeneralSettings>(obj, pi, TextColor, ButtonHover);
            }

            return base.CreatePropertyNode(obj, pi);
        }

        private class VoicesPropertyNode : ListPropertyNode<GeneralSettings>
        {
            public VoicesPropertyNode(GeneralSettings obj, PropertyInfo pi, Color textColor, Color hover)
                : base(obj, pi, textColor, hover)
            {
            }

            protected override void ShowMouseMenu()
            {
                if (Options == null)
                {
                    Options = PlatformTools.GetSpeakerVoices().OfType<object>().ToList();
                }
                base.ShowMouseMenu();
            }
        }
    }


    class KeyboardShortcutsEditor : ObjectEditorNode<KeyboardShortcuts>
    {
        public Channel[] Channels { get; private set; }

        public KeyboardShortcutsEditor(KeyboardShortcuts toEdit)
            : base(toEdit, false, true, false)
        {
        }

        protected override PropertyNode<KeyboardShortcuts> CreatePropertyNode(KeyboardShortcuts obj, PropertyInfo pi)
        {
            if (pi.PropertyType == typeof(ShortcutKey))
            {
                return new ShortcutKeyPropertyNode<KeyboardShortcuts>(obj, pi, Theme.Current.Editor.Text.XNA);
            }

            return base.CreatePropertyNode(obj, pi);
        }

        class ShortcutKeyPropertyNode<T> : StaticTextPropertyNode<T>
        {
            public ShortcutKeyPropertyNode(T obj, PropertyInfo pi, Color textColor)
                : base(obj, pi, textColor)
            {
                //var regex = new System.Text.RegularExpressions.Regex(".*ChannelGroup([0-9]+)");

                //if (regex.IsMatch(pi.Name))
                //{
                //    var groups = regex.Match(pi.Name).Groups;
                //    var match = groups[1];

                //    int channelGroupIndex = 0;
                //    if (int.TryParse(match.Value, out channelGroupIndex))
                //    {
                //        channelGroupIndex--;

                //        Channel[] c = keyMapSettingsEditor.Channels.GetChannelGroup(channelGroupIndex);

                //        if (c != null)
                //        {
                //            SetName(pi.Name.CamelCaseToHuman() + " (" + String.Join(", ", c.Select(r => r.ToStringShort())) + ")");
                //        }
                //    }
                //}
            }

            public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
            {
                if (mouseInputEvent.EventType == MouseInputEvent.EventTypes.Button)
                {
                    GetLayer<PopupLayer>().Popup(new KeybindNode((k) =>
                    {
                        if (k != null)
                        {
                            SetValue(k);
                        }
                    }));
                    return true;
                }

                return base.OnMouseInput(mouseInputEvent);
            }
        }

        public class KeybindNode : AspectNode
        {
            private Action<ShortcutKey> onFinished;

            public KeybindNode(Action<ShortcutKey> onFinished)
                : this("Press a new key combination", Theme.Current.Editor.Foreground.XNA, Theme.Current.Editor.Text.XNA, onFinished)
            {
            }

            public KeybindNode(string message, Color background, Color text, Action<ShortcutKey> onFinished)
            {
                AspectRatio = (message.Length / 40.0f) * 4f;

                Alignment = RectangleAlignment.Center;
                RelativeBounds = new RectangleF(0, 0.4f, 1, 0.1f);

                ColorNode backgroundNode = new ColorNode(background);
                AddChild(backgroundNode);

                TextNode questionNode = new TextNode(message, text);
                questionNode.RelativeBounds = new RectangleF(0.025f, 0.1f, 0.95f, 0.3f);
                backgroundNode.AddChild(questionNode);

                Node buttonsContainer = new Node();
                buttonsContainer.RelativeBounds = new RectangleF(0.1f, 0.5f, 0.8f, 0.4f);
                backgroundNode.AddChild(buttonsContainer);

                AlignHorizontally(0.1f, buttonsContainer.Children.ToArray());

                this.onFinished = onFinished;
            }

            public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
            {
                if (inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.LeftShift ||
                    inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.RightShift ||
                    inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.LeftAlt ||
                    inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.RightAlt ||
                    inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.LeftControl ||
                    inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.RightControl)
                {
                    // ignore any modifier key.
                    return true;
                }

                ShortcutKey key = new ShortcutKey(inputEvent);

                if (key.Key == Microsoft.Xna.Framework.Input.Keys.Escape)
                {
                    onFinished(null);
                }
                else
                {
                    onFinished(key);
                }

                Dispose();

                return base.OnKeyboardInput(inputEvent);
            }
        }

    }

    class ThemeSettingsEditor : ObjectEditorNode<Theme>
    {
        public AspectNode Demo { get; private set; }

        public TextButtonNode OpenDir { get; private set; }

        public ThemeSettingsEditor(IEnumerable<Theme> toEdit)
            : base(toEdit, false, true, false)
        {
            heading.Text = "Themes";
            okButton.Text = "Set Theme";
            OnOK += ThemeSettingsEditor_OnOK;

            OpenDir = new TextButtonNode("Open Theme Dir", ButtonBackground, ButtonHover, TextColor);
            OpenDir.OnClick += (mie) =>
            {
                if (Selected != null)
                {
                    PlatformTools.OpenFileManager(Selected.Filename);
                }
            };
            buttonContainer.AddChild(OpenDir);

            Node[] buttons = new Node[] { OpenDir, cancelButton, okButton };
            buttonContainer.SetOrder(buttons);

            AlignVisibleButtons();
        }

        private void ThemeSettingsEditor_OnOK(BaseObjectEditorNode<Theme> obj)
        {
            GeneralSettings.Instance.Theme = Selected.Name;
            GeneralSettings.Write();
        }

        public override void SetObjects(IEnumerable<Theme> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            if (Demo != null)
            {
                Demo.Dispose();
            }

            Theme.LocaliseFilenames(toEdit);

            Demo = new AspectNode();
            Demo.SetAspectRatio(16, 9);
            Demo.Alignment = RectangleAlignment.Center;
            right.AddChild(Demo);

            base.SetObjects(toEdit, addRemove, cancelButton);

            Demo.RelativeBounds = new RectangleF(objectProperties.RelativeBounds.X, objectProperties.RelativeBounds.Y, objectProperties.RelativeBounds.Width, 0.3f);

            objectProperties.Translate(0, Demo.RelativeBounds.Height);
            objectProperties.AddSize(0, -Demo.RelativeBounds.Height);
        }

        protected override void SetSelected(Theme theme)
        {
            base.SetSelected(theme);

            UpdateDemo(theme);
        }

        protected override PropertyNode<Theme> CreatePropertyNode(Theme obj, PropertyInfo pi)
        {
            return new StaticTextPropertyNode<Theme>(obj, pi, TextColor);
        }

        private void UpdateDemo(Theme theme)
        {
            if (theme == null)
                return;
            Demo.ClearDisposeChildren();

            ColorNode background = new ColorNode(theme.Background);
            Demo.AddChild(background);

            ColorNode leftside = new ColorNode(theme.LeftPilotList.Background);
            leftside.RelativeBounds = new RectangleF(0, 0, 0.15f, 1);
            Demo.AddChild(leftside);

            ColorNode rightside = new ColorNode(theme.RightControls.Background);
            rightside.RelativeBounds = new RectangleF(1 - 0.035f, 0, 0.035f, 1);
            Demo.AddChild(rightside);

            ColorNode topba = new ColorNode(theme.TopPanel);
            topba.RelativeBounds = new RectangleF(0, 0, 1, 0.1f);
            Demo.AddChild(topba);

            TextNode topbarText = new TextNode("Event information", theme.TopPanelText.XNA);
            topbarText.RelativeBounds = new RectangleF(0.5f, 0.5f, 0.5f, 0.5f);
            topbarText.Alignment = RectangleAlignment.BottomLeft;
            topbarText.Style.Border = theme.TopPanelTextBorder;
            topba.AddChild(topbarText);
        }
    }

    public class CustomRoundEditor : ObjectEditorNode<RoundPlan>
    {
        public EventManager EventManager { get; private set; }
        public Round CallingRound { get; private set; }

        public CustomRoundEditor(EventManager ev, Round callingRound)
        {
            CallingRound = callingRound;
            EventManager = ev;

            RoundPlan customRoundDescriptor = new RoundPlan(ev, callingRound);
            SetObject(customRoundDescriptor);

            Scale(0.8f);

            SetHeadingButtonsHeight(0.05f, 0.05f);
            heading.Text = "Create Custom Round";
            CheckVisible();
        }

        protected override IEnumerable<PropertyNode<RoundPlan>> CreatePropertyNodes(RoundPlan obj, PropertyInfo pi)
        {
            if (pi.Name == "Channels")
            {
                return AddChannelPropertyNodes(obj, pi);
            }

            if (pi.Name == "Pilots")
            {
                return AddPilotPropertyNodes(obj, pi);
            }

            return base.CreatePropertyNodes(obj, pi);
        }

        private IEnumerable<PropertyNode<RoundPlan>> AddChannelPropertyNodes(RoundPlan obj, PropertyInfo pi)
        {
            foreach (Channel channel in EventManager.Channels)
            {
                ArrayContainsPropertyNode<RoundPlan, Channel> acpn = new ArrayContainsPropertyNode<RoundPlan, Channel>(obj, pi, channel, TextColor, ButtonHover);
                yield return acpn;
            }
        }

        private IEnumerable<PropertyNode<RoundPlan>> AddPilotPropertyNodes(RoundPlan obj, PropertyInfo pi)
        {
            foreach (Pilot pilot in EventManager.Event.Pilots)
            {
                ArrayContainsPropertyNode<RoundPlan, Pilot> acpn = new ArrayContainsPropertyNode<RoundPlan, Pilot>(obj, pi, pilot, TextColor, ButtonHover);
                yield return acpn;
            }
        }

        protected override void ChildValueChanged(Change newChange)
        {
            CheckVisible();
            base.ChildValueChanged(newChange);
        }

        private void CheckVisible()
        {
            foreach (var propertyNode in GetPropertyNodes)
            {
                if (propertyNode == null)
                    continue;
                CheckVisible(propertyNode, Selected);
            }
        }

        private void CheckVisible(PropertyNode<RoundPlan> propertyNode, RoundPlan obj)
        {
            string name = propertyNode.PropertyInfo.Name;
            if (name == "ChannelChange")
            {
                if (obj.PilotSeeding == RoundPlan.PilotOrderingEnum.MinimisePreviouslyFlown)
                {
                    propertyNode.Visible = true;
                }
                else
                {
                    propertyNode.Visible = false;
                }
            }

            if (name == "NumberOfRaces")
            {
                if (obj.AutoNumberOfRaces)
                {
                    propertyNode.Visible = false;
                }
                else
                {
                    propertyNode.Visible = true;
                }
            }

            if (propertyNode is ArrayContainsPropertyNode<RoundPlan, Channel>)
            {
                if (obj.ChannelChange == RoundPlan.ChannelChangeEnum.Change)
                {
                    propertyNode.Visible = true;
                }
                else
                {
                    propertyNode.Visible = false;
                }
            }
        }
    }

    public class ComPortPropertyNode<T> : ListPropertyNode<T>
    {
        public ComPortPropertyNode(T obj, PropertyInfo pi, Color textColor, Color hoverColor)
            : base(obj, pi, textColor, hoverColor, new object[0])
        {
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Left)
            {
                string[] ports = System.IO.Ports.SerialPort.GetPortNames();

                if (!ports.Any())
                {
                    ports = new string[] { "No Com port found." };
                }
                Options = ports.OfType<object>().ToList();
            }
            return base.OnMouseInput(mouseInputEvent);
        }
    }

    public class OBSRemoteControlEditor : ObjectEditorNode<OBSRemoteControlManager.OBSRemoteControlEvent>
    {
        public OBSRemoteControlManager.OBSRemoteControlConfig Config { get; private set; }

        private BaseObjectEditorNode<OBSRemoteControlManager.OBSRemoteControlConfig> commonProperties;
        private Node commonPropertiesBackground;
        private TextNode triggersHeading;

        public OBSRemoteControlEditor(OBSRemoteControlManager.OBSRemoteControlConfig config)
        {
            Scale(0.8f, 1f);
            Config = config;

            commonPropertiesBackground = new Node();
            root.AddChild(commonPropertiesBackground);

            commonProperties = new BaseObjectEditorNode<OBSRemoteControlManager.OBSRemoteControlConfig>(Theme.Current.Editor.Background.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA, Theme.Current.ScrollBar.XNA, false);
            commonProperties.SetObject(Config, false, false);
            commonPropertiesBackground.AddChild(commonProperties);
            commonProperties.SetHeadingText("");

            commonPropertiesBackground.RelativeBounds = new RectangleF(objectProperties.RelativeBounds.X, objectProperties.RelativeBounds.Y, objectProperties.RelativeBounds.Width, 0.12f);
            commonPropertiesBackground.Scale(0.5f, 1);

            triggersHeading = new TextNode("Triggers", Theme.Current.Editor.Text.XNA);
            triggersHeading.RelativeBounds = new RectangleF(objectProperties.RelativeBounds.X, commonPropertiesBackground.RelativeBounds.Bottom + 0.02f, objectProperties.RelativeBounds.Width, 0.03f);
            root.AddChild(triggersHeading);

            SetObjects(config.RemoteControlEvents, true, true);
        }

        protected override void AddOnClick(MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            mouseMenu.AddItem("Add Scene Change", () => { AddNew(new OBSRemoteControlManager.OBSRemoteControlSetSceneEvent()); });
            mouseMenu.AddItem("Add Source Filter Toggle", () => { AddNew(new OBSRemoteControlManager.OBSRemoteControlSourceFilterToggleEvent()); });
            mouseMenu.Show(addButton);
        }

        public override void SetObjects(IEnumerable<OBSRemoteControlManager.OBSRemoteControlEvent> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            itemName.Visible = false;
            base.SetObjects(toEdit, addRemove, cancelButton);
           
            container.Translate(0, triggersHeading.RelativeBounds.Bottom);
            container.AddSize(0, -triggersHeading.RelativeBounds.Bottom);
        }

        protected override PropertyNode<OBSRemoteControlManager.OBSRemoteControlEvent> CreatePropertyNode(OBSRemoteControlManager.OBSRemoteControlEvent obj, PropertyInfo pi)
        {
            if (pi.Name == "SceneName")
            {
                return new OBSRemoteControlScenePropertyNode(obj, pi, Theme.Current.Editor.Text.XNA, Theme.Current.Hover.XNA, Config, OBSRemoteControlScenePropertyNode.Types.Scene);
            }
            else if (pi.Name == "SourceName")
            {
                return new OBSRemoteControlScenePropertyNode(obj, pi, Theme.Current.Editor.Text.XNA, Theme.Current.Hover.XNA, Config, OBSRemoteControlScenePropertyNode.Types.Source);
            }
            else if (pi.Name == "FilterName")
            {
                return new OBSRemoteControlScenePropertyNode(obj, pi, Theme.Current.Editor.Text.XNA, Theme.Current.Hover.XNA, Config, OBSRemoteControlScenePropertyNode.Types.SourceFilter);
            }

            return base.CreatePropertyNode(obj, pi);
        }


        private class OBSRemoteControlScenePropertyNode : ListPropertyNode<OBSRemoteControlManager.OBSRemoteControlEvent>
        {
            public OBSRemoteControlManager.OBSRemoteControlConfig Config { get; private set; }

            public enum Types
            {
                Scene,
                Source,
                SourceFilter
            }
            public Types OBSType { get; private set; }

            private OBSRemoteControl oBSRemoteControl;

            public OBSRemoteControlScenePropertyNode(OBSRemoteControlManager.OBSRemoteControlEvent obj, PropertyInfo pi, Color textColor, Color hover, OBSRemoteControlManager.OBSRemoteControlConfig config, Types type)
                : base(obj, pi, textColor, hover)
            {
                Config = config;
                Value.CanEdit = true;
                OBSType = type;

                oBSRemoteControl = new OBSRemoteControl(Config.Host, Config.Port, Config.Password);
            }

            protected override void ShowMouseMenu()
            {
                switch (OBSType)
                {
                    case Types.Scene:
                        oBSRemoteControl?.GetScenes(ShowMouseMenu);
                        break;
                    case Types.Source:
                        oBSRemoteControl?.GetSources(ShowMouseMenu);
                        break;
                    case Types.SourceFilter:
                        oBSRemoteControl?.GetFilters(ShowMouseMenu);
                        break;
                }
            }

            private void ShowMouseMenu(string[] options)
            {
                Options = options.OfType<object>().ToList();
                base.ShowMouseMenu();
            }
        }
    }
}
