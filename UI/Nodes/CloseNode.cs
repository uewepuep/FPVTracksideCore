using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Nodes
{
    public class CloseNode : ImageNode, IButtonNode
    {
        public CloseNode()
            : base(@"img\close.png")
        {
            KeepAspectRatio = true;
        }

        public event MouseInputDelegate OnClick;

        public override void Layout(Rectangle parentBounds)
        {
            int width = 30;
            int height = 30;
            if (Texture != null)
            {
                width = Texture.Width;
                height = Texture.Height;
            }

            Bounds = new Rectangle(parentBounds.Right - width, parentBounds.Top, width, height);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            Layout(Parent.Bounds);

            base.Draw(id, parentAlpha);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                OnClick?.Invoke(mouseInputEvent);
                return true;
            }
            return base.OnMouseInput(mouseInputEvent);
        }
    }
}
