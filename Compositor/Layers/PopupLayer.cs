using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace Composition.Layers
{
    public class PopupLayer : CompositorLayer, IPopupProvider
    {

        private PopupBackgroundNode background;

        public Node ActiveNode
        {
            get
            {
                int index = background.ChildCount - 1;
                if (index >= 0)
                {
                    return background.GetChild(index);
                }

                return null;
            }
        }

        private MenuLayer menuLayer;

        public Color PopupBackground { get; set; }
        public Color PopupButtonBackground { get; set; }
        public Color PopupHover { get; set; }
        public Color PopupText { get; set; }


        public PopupLayer(GraphicsDevice device, Color bg, Color buttonbg, Color hover, Color text) 
            : base(device)
        {
            PopupBackground = bg;
            PopupButtonBackground = buttonbg;
            PopupHover = hover;
            PopupText = text;

            background = new PopupBackgroundNode();
            Root.AddChild(background);
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            background.Visible = background.ChildCount > 0;
            base.OnUpdate(gameTime);
        }

        public override bool OnMouseInput(MouseInputEvent inputEvent)
        {
            if (ActiveNode != null)
            {
                bool result = ActiveNode.OnMouseInput(inputEvent);
            
                // No input events go to lower layers.
                return true;
            }
            return false;
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (background.ChildCount != 0)
            {
                bool result = base.OnKeyboardInput(inputEvent);

                // No input events go to lower layers.
                return true;
            }
            return false;
        }

        public void PopupConfirmation(string question, Action onOk = null)
        {
            ConfirmationNode cfn = new ConfirmationNode(question, PopupBackground, PopupButtonBackground, PopupHover, PopupText, onOk);
            Popup(cfn);
        }

        public void PopupMessage(string message, Action onOk = null)
        {
            if (onOk == null)
                onOk = () => { };

            MessageNode cfn = new MessageNode(message, PopupBackground, PopupButtonBackground, PopupHover, PopupText, onOk);
            Popup(cfn);
        }

        public void PopupCombinedMessage(string message, Action onOk = null)
        {
            if (onOk == null)
                onOk = () => { };

            CombinedMessageNode existing = background.Children.OfType<CombinedMessageNode>().FirstOrDefault();
            if (existing != null)
            {
                existing.AddMessage(message);
            }
            else
            {
                CombinedMessageNode cfn = new CombinedMessageNode(message, PopupBackground, PopupButtonBackground, PopupHover, PopupText, onOk);
                Popup(cfn);
            }
        }


        public void PopupError(string message, Exception exception, Action onOk = null)
        {
            if (onOk == null)
                onOk = () => { };

            ErrorMessageNode cfn = new ErrorMessageNode(message, exception, PopupBackground, PopupButtonBackground, PopupHover, PopupText, onOk);
            Popup(cfn);
        }

        public void Popup(Node n)
        {
            FocusedNode = n;
            background.AddChild(n);
            RequestLayout();
        }
    }

    class PopupBackgroundNode : Node
    {
        private ColorNode backgroundNode;

        public PopupBackgroundNode() 
        {
            backgroundNode = new ColorNode(Color.FromNonPremultiplied(8, 8, 8, 128));
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            DrawChildren(id, parentAlpha);
        }

        public override void Layout(RectangleF parentBounds)
        {
            base.Layout(parentBounds);
            backgroundNode.Layout(parentBounds);
        }

        public override void DrawChildren(Drawer id, float parentAlpha)
        {
            Node[] children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                Node n = children[i];

                // Draw background last only
                if (n == backgroundNode)
                    continue;

                if (i == children.Length - 1)
                {
                    backgroundNode.Draw(id, parentAlpha * Alpha);
                }

                if (n.Drawable)
                {
                    n.Draw(id, parentAlpha * Alpha);
                }
            }
        }
    }
}
