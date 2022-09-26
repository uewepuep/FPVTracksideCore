using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class ConfirmationNode : AspectNode
    {
        public TextButtonNode OK { get; private set; }
        public TextButtonNode Cancel { get; private set; }

        public ConfirmationNode(string question, MenuLayer MenuLayer, System.Action onOk)
            :this(question, MenuLayer.Background, MenuLayer.DisabledText, MenuLayer.Hover, MenuLayer.Text, onOk)
        {
        }

        public ConfirmationNode(string question, Color background, Color buttonBG, Color hover, Color text, System.Action onOk)
            : base(4)
        {
            Alignment = RectangleAlignment.Center;
            RelativeBounds = new RectangleF(0.4f, 0, 0.2f, 1);

            ColorNode backgroundNode = new ColorNode(background);
            AddChild(backgroundNode);

            TextNode questionNode = new TextNode(question, text);
            questionNode.RelativeBounds = new RectangleF(0.025f, 0.1f, 0.95f, 0.3f);
            questionNode.Alignment = RectangleAlignment.Center;
            backgroundNode.AddChild(questionNode);

            Node buttonsContainer = new Node();
            buttonsContainer.RelativeBounds = new RectangleF(0.1f, 0.5f, 0.8f, 0.4f);
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
                onOk?.Invoke();
                Dispose();
            };
        }
    }

    public class MessageNode : AspectNode
    {
        public TextButtonNode OK { get; private set; }

        public MessageNode(string message, MenuLayer MenuLayer, System.Action onOk)
            : this(message, MenuLayer.Background, MenuLayer.DisabledText, MenuLayer.Hover, MenuLayer.Text, onOk)
        {
        }

        public MessageNode(string message, Color background, Color buttonBG, Color hover, Color text, System.Action onOk)
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

            OK = new TextButtonNode("Ok", buttonBG, hover, text);

            buttonsContainer.AddChild(OK);

            AlignHorizontally(0.1f, buttonsContainer.Children.ToArray());

            OK.OnClick += (mie) =>
            {
                onOk?.Invoke();
                Dispose();
            };
        }
    }
}
