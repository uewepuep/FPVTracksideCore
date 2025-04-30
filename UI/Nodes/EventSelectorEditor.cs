using Composition;
using Composition.Input;
using Composition.Nodes;
using ExternalData;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class EventEditor : ObjectEditorNode<SimpleEvent>
    {
        public EventEditor(Event eventa) 
            :this(new SimpleEvent[] { new SimpleEvent(eventa) }, false, false)
        { 
        }

        protected EventEditor(IEnumerable<SimpleEvent> events, bool addRemove = true, bool cancelButtona = false)
            :base(events, addRemove, cancelButtona, false)
        {
            Text = "Select an event";
        }

        protected override void CreatePropertyNodes(SimpleEvent obj, IEnumerable<PropertyInfo> propertyInfos)
        {
            var ps = propertyInfos.ToList();

            var external = ps.FirstOrDefault(r => r.Name == "ExternalID");
            if (external != null)
            {
                ps.Remove(external);
                ps.Add(external);
            }

            base.CreatePropertyNodes(obj, ps);
        }

        protected override PropertyNode<SimpleEvent> CreatePropertyNode(SimpleEvent obj, PropertyInfo pi)
        {
            if (pi.Name == "ClubName")
            {
                string name = pi.GetValue(obj) as string;
                if (!string.IsNullOrEmpty(name))
                {
                    var property = new StaticTextPropertyNode<SimpleEvent>(obj, pi, TextColor);

                    if (obj.SyncWithMultiGP)
                    {
                        property.NameNode.Text = "Chapter";
                    }

                    return property;
                }
                else
                {
                    return null;
                }
            }

            if (pi.Name.Contains("FPVTrackside"))
            {
                bool value = (bool)pi.GetValue(obj);

                if (!CheckLogin(SyncType.FPVTrackside) && !value)
                {
                    pi.SetValue(obj, false);
                    return null;
                }
            }

            if (pi.Name.Contains("MultiGP"))
            {
                object objv = pi.GetValue(obj);
                if (objv is bool)
                {
                    bool value = (bool)objv;

                    if (!CheckLogin(SyncType.MultiGP) && !value)
                    {
                        pi.SetValue(obj, false);
                        return null;
                    }

                    if (obj.ExternalID == 0)
                    {
                        pi.SetValue(obj, false);
                        return null;
                    }
                }
            }

            if (pi.Name.Contains("IsGQ"))
            {
                object objv = pi.GetValue(obj);
                if (objv is bool)
                {
                    bool value = (bool)objv;

                    if (!value)
                        return null;
                }
            }

            PropertyNode<SimpleEvent> n = base.CreatePropertyNode(obj, pi);


            if (obj.RulesLocked)
            {
                CategoryAttribute cat = pi.GetCustomAttribute<CategoryAttribute>();
                BoolPropertyNode<SimpleEvent> boolPropertyNode = n as BoolPropertyNode<SimpleEvent>;
                TextPropertyNode<SimpleEvent> textPropertyNode = n as TextPropertyNode<SimpleEvent>;

                if (cat != null && textPropertyNode != null && cat.Category == "Race Rules")
                {
                    textPropertyNode.Locked = true;
                }

                string[] boolProperties = new string[] { "MultiGP", "RulesLocked", "IsGQ", "VisibleOnline", "SyncWithFPVTrackside" };
                foreach (string b in boolProperties)
                {
                    if (boolPropertyNode != null && pi.Name.Contains(b))
                    {
                        boolPropertyNode.Locked = true;
                    }
                }
            }

            return n;
        }

        protected virtual bool CheckLogin(SyncType syncType)
        {
            return false;
        }
    }


    public class EventSelectorEditor : EventEditor
    {
        public override bool GroupItems { get { return true; } }

        public event System.Action GeneralSettingsSaved;

        public TextButtonNode CloneButton { get; private set; }
        public TextButtonNode RecoverButton { get; private set; }

        public Profile Profile { get { return MenuButton.Profile; } }

        public MenuButton MenuButton { get; private set; }

        public EventSelectorEditor(Texture2D logo, Profile profile)
            : this(new SimpleEvent[0], true, false)
        {
            heading.RelativeBounds = new RectangleF(0, 0.18f, 1, 0.05f);
            container.RelativeBounds = new RectangleF(0, heading.RelativeBounds.Bottom, 1, 1 - heading.RelativeBounds.Bottom);

            RelativeBounds = new RectangleF(0.2f, 0.01f, 0.6f, 0.98f);

            ColorNode colorNode = new ColorNode(Theme.Current.TopPanel.XNA);
            colorNode.RelativeBounds = new RectangleF(0, 0, 1, 0.175f);
            AddChild(colorNode);

            ImageNode logoNode = new ImageNode(logo);
            logoNode.RelativeBounds = new RectangleF(0, 0, 1, 0.99f);
            logoNode.Alignment = RectangleAlignment.TopCenter;
            colorNode.AddChild(logoNode);

            addButton.Text = "New";
            okButton.Text = "Open";

            MenuButton = new MenuButton(profile, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            MenuButton.RelativeBounds = new RectangleF(0.96f, 0.01f, 0.025f, 0.025f);
            MenuButton.GeneralSettingsSaved += () => { GeneralSettingsSaved?.Invoke(); };
            MenuButton.Restart += (e) =>
            {
                if (CompositorLayer.LayerStack.Game is UI.BaseGame)
                {
                    ((UI.BaseGame)CompositorLayer.LayerStack.Game).Restart(e);
                }
            };

            MenuButton.ProfileSet += MenuButton_ProfileSet;
            AddChild(MenuButton);

            SetObjects(GetEvents(profile), true);
            AlignVisibleButtons();
        }

        private void MenuButton_ProfileSet(Profile profile)
        {
            MenuButton.Profile = profile;
            GeneralSettings.Instance.Profile = profile.Name;
            GeneralSettings.Write();
        }

        public EventSelectorEditor(IEnumerable<SimpleEvent> events, bool addRemove = true, bool cancelButtona = false)
           : base(events.Where(e => e.Enabled), addRemove, cancelButtona)
        {
            OnOK += EventEditor_OnOK;

            Selected = Objects.OrderByDescending(e => e.LastOpened).FirstOrDefault();

            if (addRemove)
            {
                RecoverButton = new TextButtonNode("Recover", ButtonBackground, ButtonHover, TextColor);
                RecoverButton.OnClick += (mie) => { Recover(); };
                RecoverButton.Visible = events.Any(r => !r.Enabled);
                buttonContainer.AddChild(RecoverButton);

                CloneButton = new TextButtonNode("Clone", ButtonBackground, ButtonHover, TextColor);
                CloneButton.OnClick += Clone;
                buttonContainer.AddChild(CloneButton);
            }
            itemName.Visible = false;

            AlignVisibleButtons();
        }

        private void Clone(MouseInputEvent mie)
        {
            if (Selected != null)
            {
                Event loaded = null;

                using (IDatabase db = DatabaseFactory.Open(Selected.ID))
                {
                    loaded = db.LoadEvent();
                }
                if (loaded != null)
                {
                    Event newEvent = loaded.Clone();
                    using (IDatabase db = DatabaseFactory.Open(newEvent.ID))
                    {
                        db.Insert(newEvent);

                        db.LoadEvent();
                        db.Insert(newEvent.Pilots);

                        AddNew(new SimpleEvent(newEvent));
                    }
                }
            }
        }

        protected override PropertyNode<SimpleEvent> CreatePropertyNode(SimpleEvent obj, PropertyInfo pi)
        {
            if (pi.Name == "VisibleOnline")
            {
                if (!obj.SyncWithFPVTrackside)
                    return null;
            }

            if (pi.Name == "RulesLocked")
            {
                if (obj.RulesLocked)
                {
                    if (obj.SyncWithMultiGP)
                    {
                        var bp = new BoolPropertyNode<SimpleEvent>(obj, pi, TextColor, ButtonHover);
                        bp.Locked = true;
                        bp.Name = "Rules locked";

                        return bp;
                    }
                }
                else
                {
                    return null;
                }
            }
            return base.CreatePropertyNode(obj, pi);
        }

        public void DisableButtons()
        {
            foreach (Node n in buttonContainer.Children)
            {
                TextButtonNode tbn = n as TextButtonNode;
                if (tbn != null)
                {
                    tbn.Enabled = false;
                }
            }
        }

        protected override string ItemToGroupString(SimpleEvent item)
        {
            return item.Month;
        }

        public override IEnumerable<SimpleEvent> Order(IEnumerable<SimpleEvent> ts)
        {
            return ts.OrderByDescending(e => e.Start);
        }

        protected override void Remove(MouseInputEvent mie)
        {
            SimpleEvent selected = Selected;
            if (selected != null)
            {
                selected.Enabled = false;
                ConvertSaveEvent(selected);
            }

            base.Remove(mie);
            RecoverButton.Visible = true;
            AlignVisibleButtons();
        }

        protected override void AddOnClick(MouseInputEvent mie)
        {
            Event eve = CreateNewEvent();
            AddNew(new SimpleEvent(eve));
        }

        private Event CreateNewEvent()
        {
            Event eve;
            using (IDatabase db = DatabaseFactory.Open(Guid.Empty))
            {
                Club club = db.GetDefaultClub();

                eve = new Event();
                eve.Channels = Channel.Read(Profile);
                eve.Club = club;
                db.Insert(eve);
            }
            return eve;
        }

        private void Recover()
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            SimpleEvent[] all = GetEvents(Profile);
            IEnumerable<SimpleEvent> disabled = all.Where(e => !e.Enabled).OrderByDescending(e => e.Start);

            foreach (var obj in disabled)
            {
                mouseMenu.AddItem(obj.Name, () => { Recover(obj); });
            }

            mouseMenu.Show(RecoverButton);
        }

        private void Recover(SimpleEvent recover)
        {
            recover.Enabled = true;
            ConvertSaveEvent(recover);

            SetObjects(GetEvents(Profile), true, true);
        }

        protected SimpleEvent[] GetEvents(Profile profile)
        {
            SimpleEvent[] events;
            using (IDatabase db = DatabaseFactory.Open(Guid.Empty))
            {
                events = db.GetSimpleEvents().ToArray();

                Club club = db.All<Club>().FirstOrDefault();
                if (club == null)
                {
                    club = new Club();
                    db.Insert(club);
                }

                if (events.Length == 0)
                {
                    Event eve = CreateNewEvent();
                    events = new SimpleEvent[] { new SimpleEvent(eve) };
                }
            }
            return events;
        }

        public override void SetObjects(IEnumerable<SimpleEvent> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            IEnumerable<SimpleEvent> enabled = toEdit.Where(e => e.Enabled);
            IEnumerable<SimpleEvent> disabled = toEdit.Where(e => !e.Enabled);

            base.SetObjects(enabled, addRemove, cancelButton);

            if (RecoverButton != null)
            {
                RecoverButton.Visible = disabled.Any();
                AlignVisibleButtons();
            }
        }

        private void EventEditor_OnOK(BaseObjectEditorNode<SimpleEvent> obj)
        {
            if (obj != null && obj.Selected != null)
            {
                obj.Selected.LastOpened = DateTime.Now;
            }

            SaveChanges();
        }

        public void SaveChanges()
        {
            Change[] changes = Changes;

            foreach (Change change in changes)
            {
                SimpleEvent simpleEvent = change.Object as SimpleEvent;
                if (simpleEvent != null)
                {
                    simpleEvent.Enabled = true;
                    ConvertSaveEvent(simpleEvent);
                }
            }
        }

        public void ConvertSaveEvent(SimpleEvent simpleEvent)
        {
            if (simpleEvent == null)
                return;

            using (IDatabase db = DatabaseFactory.Open(simpleEvent.ID))
            {
                Event eventt = db.LoadEvent();
                if (eventt != null)
                {
                    ReflectionTools.Copy(simpleEvent, eventt);
                }
                db.Update(eventt);
            }
        }

    }
}
