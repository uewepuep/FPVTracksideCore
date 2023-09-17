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
    public class PopupLayer : CompositorLayer
    {
        public void Popup(Node n)
        {
            FocusedNode = n;
            background.AddChild(n);
            RequestLayout();
        }

        private ColorNode background;

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

        public PopupLayer(GraphicsDevice device) 
            : base(device)
        {
            background = new ColorNode(Color.FromNonPremultiplied(8,8,8,128));
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

        public void PopupConfirmation(string question)
        {
            PopupConfirmation(question, () => { });
        }

        public void PopupConfirmation(string question, Action onOk)
        {
            ConfirmationNode cfn = new ConfirmationNode(question, LayerStack.GetLayer<MenuLayer>(), onOk);
            background.AddChild(cfn);
            RequestLayout();
            FocusedNode = cfn;
        }

        public void PopupConfirmationDontShowAgain(string question, Action<bool> onOkDontShow)
        {
            ConfirmationDontShowAgainNode cfn = new ConfirmationDontShowAgainNode(question, LayerStack.GetLayer<MenuLayer>(), onOkDontShow);
            background.AddChild(cfn);
            RequestLayout();
            FocusedNode = cfn;
        }

        public void PopupMessage(string message)
        {
            PopupMessage(message, () => { });
        }

        public void PopupMessage(string message, Action onOk)
        {
            MessageNode cfn = new MessageNode(message, LayerStack.GetLayer<MenuLayer>(), onOk);
            background.AddChild(cfn);
            RequestLayout();
            FocusedNode = cfn;
        }
    }
}
