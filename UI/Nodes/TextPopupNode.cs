using Composition.Layers;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI;
using UI.Nodes;

namespace Composition.Nodes
{
    public class TextPopupNode : AbsoluteSizeNode
    {
        protected TextEditNode edit;
        protected TextButtonNode ok;
        protected TextButtonNode cancel;

        public event Action<string> OnOK;

        public TextPopupNode(string title, string example, string existing)
            : base(350, 130)
        {
            BorderPanelShadowNode background = new BorderPanelShadowNode();
            AddChild(background);

            TextNode heading = new TextNode(title, Theme.Current.TextMain.XNA);
            heading.Alignment = RectangleAlignment.Center;
            background.Inner.AddChild(heading);

            Node inputContainer = new Node();

            TextNode name = new TextNode(example, Theme.Current.TextMain.XNA);
            name.RelativeBounds = new RectangleF(0, 0, 0.33f, 1);
            inputContainer.AddChild(name);

            ColorNode textboxBackground = new ColorNode(Theme.Current.Button.XNA);
            textboxBackground.RelativeBounds = new RectangleF(name.RelativeBounds.Right, 0, 1 - name.RelativeBounds.Right, 1);
            inputContainer.AddChild(textboxBackground);

            edit = new TextEditNode(existing, Theme.Current.TextMain.XNA);
            edit.OnReturn += Edit_OnReturn;
            edit.Scale(0.9f);

            textboxBackground.AddChild(edit);
            background.Inner.AddChild(inputContainer);

            Node buttonContainer = new Node();
            cancel = new TextButtonNode("Cancel", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            cancel.OnClick += (v) => { Cancel(); };
            buttonContainer.AddChild(cancel);

            ok = new TextButtonNode("Ok", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            ok.OnClick += (v) => { Ok(); };
            buttonContainer.AddChild(ok);

            AlignHorizontally(0.05f, cancel, ok);
            background.Inner.AddChild(buttonContainer);

            heading.RelativeBounds = new RectangleF(0, 0, 1, 0.30f);
            inputContainer.RelativeBounds = new RectangleF(0.1f, 0.35f, 0.8f, 0.25f);
            buttonContainer.RelativeBounds = new RectangleF(0, 0.7f, 1, 0.3f);

        }

        protected virtual void Edit_OnReturn()
        {
            Ok();
        }

        public override void SetCompositorLayer(CompositorLayer compositor)
        {
            base.SetCompositorLayer(compositor);
            edit.HasFocus = true;
        }

        protected void Ok()
        {
            if (!string.IsNullOrEmpty(edit.Text))
            {
                OnOK?.Invoke(edit.Text);
                Dispose();
            }
        }

        protected void Cancel()
        {
            Dispose();
        }
    }

    public class AddPilotNode : TextPopupNode
    {
        private EventManager ev;

        public AddPilotNode(EventManager ev)
            :base("Add Pilot", "Name", "")
        {
            this.ev = ev;
            ok.Text = "Add";

            OnOK += AddPilotNode_OnOK;
        }

        private void AddPilotNode_OnOK(string obj)
        {
            Pilot p = ev.GetCreatePilot(obj);
            ev.AddPilot(p);
        }
    }
}
