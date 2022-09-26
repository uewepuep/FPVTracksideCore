using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class BugReportNode : AspectNode
    {
        public TextButtonNode OK { get; private set; }
        public TextButtonNode Cancel { get; private set; }

        private TextEditNode description;
        public string Description { get { return description.Text; } }

        public BugReportNode(System.Action<BugReportNode> onOk)
            : base(1)
        {
            Alignment = RectangleAlignment.Center;
            RelativeBounds = new RectangleF(0.4f, 0, 0.2f, 1);


            Color background = Theme.Current.MenuBackground.XNA;
            Color hover = Theme.Current.Hover.XNA;
            Color text = Theme.Current.MenuText.XNA;
            Color buttonBG = Theme.Current.MenuTextInactive.XNA;

            ColorNode backgroundNode = new ColorNode(background);
            AddChild(backgroundNode);


            Node container = new Node();
            container.RelativeBounds = new RectangleF(0.025f, 0.025f, 0.95f, 0.2f);
            backgroundNode.AddChild(container);

            container.AddChild(new TextNode("Upload bug report", text));
            container.AddChild(new TextNode("Please enter a description below.", text));
            container.AddChild(new TextNode("The most recent 64kb of logs will be uploaded.", text));

            foreach (TextNode tn in container.Children.OfType<TextNode>())
            {
                tn.Alignment = RectangleAlignment.TopLeft;
            }

            AlignVertically(0.05f, container.Children);

            ColorNode textField = new ColorNode(buttonBG);
            textField.RelativeBounds = new RectangleF(0.025f, 0.25f, 0.95f, 0.6f);
            AddChild(textField);

            description = new TextEditNode("", text);
            description.Alignment = RectangleAlignment.TopLeft;
            description.OverrideHeight = 30;
            description.HasFocus = true;
            description.CanScale = false;
            textField.AddChild(description);

            Node buttonsContainer = new Node();
            buttonsContainer.RelativeBounds = new RectangleF(0.1f, 0.875f, 0.8f, 0.1f);
            backgroundNode.AddChild(buttonsContainer);

            OK = new TextButtonNode("Ok", buttonBG, hover, text);
            Cancel = new TextButtonNode("Cancel", buttonBG, hover, text);

            buttonsContainer.AddChild(Cancel);
            buttonsContainer.AddChild(OK);

            AlignHorizontally(0.1f, buttonsContainer.Children.ToArray());

            Cancel.OnClick += (mie) =>
            {
                Dispose();
            };

            OK.OnClick += (mie) =>
            {
                onOk?.Invoke(this);
                Dispose();
            };
        }
    }

}
