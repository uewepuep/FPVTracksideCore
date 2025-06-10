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

    public class MessageNode : AspectNode
    {
        public TextButtonNode OKButton { get; private set; }
        protected Node buttonsContainer;
        protected TextNode messageNode;

        private System.Action onOk;

        public MessageNode(string message, Color background, Color buttonBackground, Color hover, Color text, System.Action onOk)
        {
            this.onOk = onOk;

            ColorNode backgroundNode = new ColorNode(background);
            AddChild(backgroundNode);

            messageNode = new TextNode(message, text);
            backgroundNode.AddChild(messageNode);

            buttonsContainer = new Node();

            backgroundNode.AddChild(buttonsContainer);

            OKButton = new TextButtonNode("Ok", buttonBackground, hover, text);

            buttonsContainer.AddChild(OKButton);

            AlignHorizontally(0.1f, buttonsContainer.Children.ToArray());

            OKButton.OnClick += (mie) =>
            {
                OK();
            };
        }


        public virtual void OK()
        {
            onOk?.Invoke();
            Dispose();
        }

        public override void Layout(RectangleF parentBounds)
        {
            string[] lines = messageNode.Text.Split("\n");

            int length = Math.Max(50, lines.Max(l => l.Length));

            int width = 10 + (length * 10);
            int height = 100 + lines.Length * 20;

            SetAspectRatio(width, height);

            float relativeWidth = width / parentBounds.Width;
            float relativeHeight = height / parentBounds.Height;

            RelativeBounds = new RectangleF(0.5f - (relativeWidth / 2), 0.5f - (relativeHeight / 2), relativeWidth, relativeHeight);

            int targetButtonHeight = 50;
            float relButtonHeight = targetButtonHeight / (float)height;

            buttonsContainer.RelativeBounds = new RectangleF(0, 1 - relButtonHeight, 1, relButtonHeight);
            messageNode.RelativeBounds = new RectangleF(0, 0, 1, 1 - relButtonHeight);

            buttonsContainer.Scale(0.7f);
            messageNode.Scale(0.9f);

            base.Layout(parentBounds);
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
                    OK();
                    return true;
                }
            }

            return false;
        }
    }



    public class ConfirmationNode : MessageNode
    {
        public TextButtonNode CancelButton { get; private set; }

        public ConfirmationNode(string question, Color background, Color buttonBG, Color hover, Color text, System.Action onOk)
            : base(question, background, buttonBG, hover, text, onOk)
        {
            CancelButton = new TextButtonNode("Cancel", buttonBG, hover, text);

            buttonsContainer.AddChild(CancelButton);

            AlignHorizontally(0.1f, OKButton, CancelButton);

            CancelButton.OnClick += (n) => { Cancel(); };
        }

        private void Cancel()
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
                if (inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.Escape)
                {
                    Cancel();
                    return true;
                }
            }
            
            return false;
        }
    }

    public class CombinedMessageNode : MessageNode
    {
        public CombinedMessageNode(string message, Color background, Color buttonBackground, Color hover, Color text, Action onOk) 
            : base(message, background, buttonBackground, hover, text, onOk)
        {
        }

        public void AddMessage(string message)
        {
            messageNode.Text += "\n" + message;

            int lines = messageNode.Text.Split('\n').Count();

            AspectRatio = (message.Length / 40.0f) * 4f / lines;
            RequestLayout();
        }
    }

    public class ErrorMessageNode : MessageNode
    {
        public Exception Exception { get; set; }

        public TextButtonNode Copy { get; set; }

        public ErrorMessageNode(string message, Exception exception, Color background, Color buttonBackground, Color hover, Color text, Action onOk)
            : base(message, background, buttonBackground, hover, text, onOk)
        {
            Exception = exception;
            Copy = new TextButtonNode("Copy Exception", buttonBackground, hover, text);
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
