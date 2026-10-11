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

        // An optional row between the message and the buttons (e.g. a
        // "Don't show this again" tick box). Set by subclasses.
        protected Node footer;

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

            AddChild(new ShadowNode());
        }


        public virtual void OK()
        {
            onOk?.Invoke();
            Dispose();
        }

        public override void Layout(RectangleF parentBounds)
        {
            string[] lines = messageNode.Text.Split("\n");

            int length = Math.Max(30, lines.Max(l => l.Length));

            int targetButtonHeight = 50;
            int footerHeight = footer != null ? 30 : 0;

            int width = 10 + (length * 10);
            int height = targetButtonHeight + footerHeight + 10 + lines.Length * 20;

            SetAspectRatio(width, height);

            float relativeWidth = width / parentBounds.Width;
            float relativeHeight = height / parentBounds.Height;

            RelativeBounds = new RectangleF(0.5f - (relativeWidth / 2), 0.5f - (relativeHeight / 2), relativeWidth, relativeHeight);

            float relButtonHeight = targetButtonHeight / (float)height;

            float relFooterHeight = footerHeight / (float)height;

            buttonsContainer.RelativeBounds = new RectangleF(0, 1 - relButtonHeight, 1, relButtonHeight);
            messageNode.RelativeBounds = new RectangleF(0, 0, 1, 1 - relButtonHeight - relFooterHeight);

            if (footer != null)
            {
                footer.RelativeBounds = new RectangleF(0, 1 - relButtonHeight - relFooterHeight, 1, relFooterHeight);
            }

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

            AlignHorizontally(0.1f, CancelButton, OKButton);

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

    // A confirmation with a "Don't show this again" tick box. onDontShowAgain
    // is called with the tick box's state when the popup closes, whichever
    // button closed it.
    public class DontShowAgainConfirmationNode : ConfirmationNode
    {
        public CheckboxNode Checkbox { get; private set; }

        private Action<bool> onClose;
        private bool closed;

        public DontShowAgainConfirmationNode(string question, string checkboxText, Color background, Color buttonBG, Color hover, Color text, System.Action onOk, Action<bool> onDontShowAgain)
            : base(question, background, buttonBG, hover, text, onOk)
        {
            onClose = onDontShowAgain;

            footer = new Node();
            Node backgroundNode = Children.OfType<ColorNode>().First();
            backgroundNode.AddChild(footer);

            Checkbox = new CheckboxNode(hover);
            Checkbox.Tint = text;
            Checkbox.RelativeBounds = new RectangleF(0.05f, 0.15f, 0.1f, 0.7f);
            footer.AddChild(Checkbox);

            TextButtonNode label = new TextButtonNode(checkboxText, Color.Transparent, Color.Transparent, text);
            label.TextNode.Alignment = RectangleAlignment.CenterLeft;
            label.RelativeBounds = new RectangleF(0.05f + 30 / 400f, 0, 0.8f, 1);
            label.OnClick += (mie) => { Checkbox.Value = !Checkbox.Value; };
            footer.AddChild(label);
        }

        public override void Layout(RectangleF parentBounds)
        {
            base.Layout(parentBounds);

            // Keep the box at a fixed 20px with the label just after it,
            // whatever width the popup sized itself to.
            if (footer.BoundsF.Width > 0)
            {
                float boxLeft = footer.BoundsF.X + 15;
                float rel = (boxLeft - footer.BoundsF.X) / footer.BoundsF.Width;
                float relBox = 24 / footer.BoundsF.Width;
                Checkbox.RelativeBounds = new RectangleF(rel, Checkbox.RelativeBounds.Y, relBox, Checkbox.RelativeBounds.Height);
                Node label = footer.Children.OfType<TextButtonNode>().First();
                label.RelativeBounds = new RectangleF(rel + relBox + (8 / footer.BoundsF.Width), 0, 1 - (rel + relBox), 1);
                footer.Layout(footer.Parent.BoundsF);
            }
        }

        public override void Dispose()
        {
            if (!closed)
            {
                closed = true;
                onClose?.Invoke(Checkbox.Value);
            }
            base.Dispose();
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
