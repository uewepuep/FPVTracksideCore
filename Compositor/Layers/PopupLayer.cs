using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

        public Color PopupBackground { get { if (menuLayer == null) { menuLayer = LayerStack.GetLayer<MenuLayer>(); } return menuLayer.Background; } }
        public Color PopupButtonBackground { get { return menuLayer.DisabledText; } }
        public Color PopupHover { get { return menuLayer.Hover; } }
        public Color PopupText { get { return menuLayer.Text; } }


        public PopupLayer(GraphicsDevice device) 
            : base(device)
        {
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

    class PopupBackgroundNode : ColorNode
    {
        public PopupBackgroundNode() 
            : base(Color.FromNonPremultiplied(8, 8, 8, 128))
        {
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);
        }
    }
}
