using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    class ThemeEditor : ObjectEditorNode<Theme>
    {
        public AspectNode Demo { get; private set; }

        public TextButtonNode OpenDir { get; private set; }
        public Profile Profile { get; private set; }

        public ThemeEditor(Profile profile, IEnumerable<Theme> toEdit)
            : base(toEdit, false, true, false)
        {
            this.Profile = profile;
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
            ApplicationProfileSettings profileSettings = ApplicationProfileSettings.Read(Profile);
            profileSettings.Theme = Selected.Name;
            ApplicationProfileSettings.Write(Profile, profileSettings);
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
            if (pi.PropertyType == typeof(ToolColor)) 
            {
                return new ToolColorEditor<Theme>(obj, pi, TextColor);
            }
            return null;
        }

        protected override IEnumerable<PropertyNode<Theme>> CreatePropertyNodes(Theme obj, PropertyInfo inProperty)
        {
            if (typeof(PanelTheme).IsAssignableFrom(inProperty.PropertyType))
            {
                foreach (var v in GetSubObjectProperties<PanelTheme>(obj, inProperty))
                {
                    yield return new SubPropertyNode<Theme, PanelTheme>(obj, inProperty, v);
                }
            }
            else
            {
                foreach (var v in base.CreatePropertyNodes(obj, inProperty))
                {
                    yield return v;
                }
            }
        }

        private IEnumerable<PropertyNode<T>> GetSubObjectProperties<T>(Theme obj, PropertyInfo inProperty)
        {
            T value = (T)inProperty.GetValue(obj, null);
            if (value == null)
                yield break;

            foreach (PropertyInfo pi in typeof(T).GetProperties())
            {
                if (pi.PropertyType == typeof(ToolColor))
                {
                    yield return new ToolColorEditor<T>(value, pi, TextColor);
                }
            }
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

    public class ToolColorEditor<Theme> : StaticTextPropertyNode<Theme>
    {
        public ColorNode ColorNode { get; private set; }

        public ToolColorEditor(Theme obj, PropertyInfo pi, Color textColor) 
            : base(obj, pi, textColor)
        {
            ColorNode = new ColorNode(Color.Transparent);
            RectangleF rbs = Value.RelativeBounds;
            rbs.Width = 0.025f;
            rbs.X = Value.RelativeBounds.Width - rbs.Width;
            ColorNode.RelativeBounds = rbs;
            AddChild(ColorNode);

            UpdateFromObject();
        }

        public override void UpdateFromObject()
        {
            base.UpdateFromObject();

            if (ColorNode != null && Value != null)
            {
                ToolColor value = PropertyInfo.GetValue(Object, null) as ToolColor;
                ColorNode.Color = value.XNA;
            }
        }
    }

}
