using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Timing;

namespace UI.Nodes
{
    class TimingSystemEditor : ObjectEditorNode<Timing.TimingSystemSettings>
    {
        public TimingSystemEditor(IEnumerable<TimingSystemSettings> toEdit)
            : base(toEdit, true, true)
        {
            Text = "Timing Settings";
        }

        protected override void AddOnClick(MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            mouseMenu.AddItem("LapRF 8-way", () => { AddNew(new Timing.ImmersionRC.LapRFSettingsEthernet()); });
            mouseMenu.AddItem("LapRF Puck", () => { AddNew(new Timing.ImmersionRC.LapRFSettingsUSB()); });
            mouseMenu.AddItem("RotorHazard (Beta)", () => { AddNew(new Timing.RotorHazard.RotorHazardSettings()); });
            mouseMenu.AddItem("Chorus (Alpha)", () => { AddNew(new Timing.Chorus.ChorusSettings()); });
            mouseMenu.AddItem("Video Color (Alpha)", () => { AddNew(new VideoTimingSettings()); });
            mouseMenu.AddItem("Dummy (RNG) (Testing only)", () => { AddNew(new DummySettings()); });

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

            foreach (var propertyNode in GetPropertyNodes)
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
