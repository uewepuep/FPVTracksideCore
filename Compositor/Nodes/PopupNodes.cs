using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using static System.Net.Mime.MediaTypeNames;

namespace Composition.Nodes
{
    public class ConfirmationNode : AspectNode
    {
        public TextButtonNode OK { get; private set; }
        public TextButtonNode Cancel { get; private set; }

        protected Node buttonsContainer;
        protected TextNode questionNode;

        protected System.Action onOK;

        public ConfirmationNode(string question, MenuLayer MenuLayer, System.Action onOk)
            :this(question, MenuLayer.Background, MenuLayer.DisabledText, MenuLayer.Hover, MenuLayer.Text, onOk)
        {
        }

        public ConfirmationNode(string question, Color background, Color buttonBG, Color hover, Color text, System.Action onOk)
            : base(4)
        {
            onOK = onOk;

            Alignment = RectangleAlignment.Center;
            RelativeBounds = new RectangleF(0.4f, 0, 0.2f, 1);

            ColorNode backgroundNode = new ColorNode(background);
            AddChild(backgroundNode);

            questionNode = new TextNode(question, text);
            questionNode.RelativeBounds = new RectangleF(0.025f, 0.1f, 0.95f, 0.3f);
            questionNode.Alignment = RectangleAlignment.Center;
            backgroundNode.AddChild(questionNode);

            buttonsContainer = new Node();
            buttonsContainer.RelativeBounds = new RectangleF(0.1f, 0.5f, 0.8f, 0.4f);
            backgroundNode.AddChild(buttonsContainer);

            OK = new TextButtonNode("Ok", buttonBG, hover, text);
            Cancel = new TextButtonNode("Cancel", buttonBG, hover, text);

            buttonsContainer.AddChild(Cancel);
            buttonsContainer.AddChild(OK);

            AlignHorizontally(0.1f, buttonsContainer.Children.ToArray());

            Cancel.OnClick += Cancel_OnClick;

            OK.OnClick += OK_OnClick;
        }

        private void OK_OnClick(MouseInputEvent mie)
        {
            onOK?.Invoke();
            Dispose();
        }

        private void Cancel_OnClick(MouseInputEvent mie)
        {
            Dispose();
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (base.OnKeyboardInput(inputEvent))
            {
                return true;
            }

            if (inputEvent.ButtonState == ButtonStates.Pressed)
            {
                if (inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.Enter)
                {
                    OK_OnClick(null);
                    return true;
                }

                if (inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.Escape)
                {
                    Cancel_OnClick(null);
                    return true;
                }
            }
            
            return false;
        }
    }

    public class ConfirmationDontShowAgainNode : ConfirmationNode
    {
        public CheckboxNode DontShowAgain { get; private set; }

        public ConfirmationDontShowAgainNode(string question, MenuLayer MenuLayer, Action<bool> onOkDontShowAgain)
            : base(question, MenuLayer.Background, MenuLayer.DisabledText, MenuLayer.Hover, MenuLayer.Text, null)
        {
            RelativeBounds = new RectangleF(0.4f, 0, 0.3f, 1);

            buttonsContainer.RelativeBounds = new RectangleF(0.1f, 0.6f, 0.8f, 0.275f);
            questionNode.RelativeBounds = new RectangleF(0.025f, 0.09f, 0.95f, 0.2f);

            Node container = new Node();
            container.RelativeBounds = new RectangleF(0.1f, 0.38f, 0.8f, 0.125f);
            AddChild(container);

            DontShowAgain = new CheckboxNode();
            DontShowAgain.RelativeBounds = new RectangleF(0.55f, 0.0f, 0.1f, 1f);
            container.AddChild(DontShowAgain);

            TextNode textNode = new TextNode("Don't show again", MenuLayer.Text);
            textNode.RelativeBounds = new RectangleF(0.325f, 0.0f, 0.9f, 1);
            textNode.Alignment = RectangleAlignment.CenterLeft;
            container.AddChild(textNode);

            OK.OnClick += (mie) =>
            {
                onOkDontShowAgain(DontShowAgain.Value);
            };
        }
    }

    public class MessageNode : AspectNode
    {
        public TextButtonNode OK { get; private set; }
        protected Node buttonsContainer;

        public MessageNode(string message, MenuLayer MenuLayer, System.Action onOk)
        {
            AspectRatio = (message.Length / 40.0f) * 4f;

            Alignment = RectangleAlignment.Center;
            RelativeBounds = new RectangleF(0, 0.4f, 1, 0.1f);

            ColorNode backgroundNode = new ColorNode(MenuLayer.Background);
            AddChild(backgroundNode);

            TextNode questionNode = new TextNode(message, MenuLayer.Text);
            questionNode.RelativeBounds = new RectangleF(0.025f, 0.1f, 0.95f, 0.3f);
            backgroundNode.AddChild(questionNode);

            buttonsContainer = new Node();

            buttonsContainer.RelativeBounds = new RectangleF(0.1f, 0.5f, 0.8f, 0.4f);
            backgroundNode.AddChild(buttonsContainer);

            OK = new TextButtonNode("Ok", MenuLayer.DisabledText, MenuLayer.Hover, MenuLayer.Text);

            buttonsContainer.AddChild(OK);

            AlignHorizontally(0.1f, buttonsContainer.Children.ToArray());

            OK.OnClick += (mie) =>
            {
                onOk?.Invoke();
                Dispose();
            };
        }
    }

    public class ErrorMessageNode : MessageNode
    {
        public Exception Exception { get; set; }

        public TextButtonNode Copy { get; set; }

        public ErrorMessageNode(string message, Exception exception, MenuLayer MenuLayer, Action onOk)
            : base(message, MenuLayer, onOk)
        {
            Exception = exception;
            Copy = new TextButtonNode("Copy Exception", MenuLayer.DisabledText, MenuLayer.Hover, MenuLayer.Text);
            buttonsContainer.AddChild(Copy);
            Copy.OnClick += Copy_OnClick;

            AlignHorizontally(0.1f, buttonsContainer.Children.ToArray());
            RequestLayout();
        }

        private void Copy_OnClick(MouseInputEvent mie)
        {
            PlatformTools.Clipboard.SetText(Exception.ToString());
        }
    }
}
