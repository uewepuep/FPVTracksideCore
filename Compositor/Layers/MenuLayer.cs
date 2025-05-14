using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Composition.Layers
{
    public class MenuLayer : CompositorLayer
    {
        public Color Background { get; private set; }
        public Color Hover { get; private set; }
        public Color Text { get; private set; }
        public Color DisabledText { get; private set; }
        public Color ScrollBar { get; private set; }

        public MenuLayer(GraphicsDevice device, Color background, Color hover, Color text, Color disabledText, Color scrollbar)
            : base(device)
        {
            Background = background;
            Hover = hover;
            Text = text;
            DisabledText = disabledText;
            ScrollBar = scrollbar;
        }

        public override bool OnMouseInput(MouseInputEvent inputEvent)
        {
            bool result = base.OnMouseInput(inputEvent);

            if (!result && inputEvent.ButtonState == ButtonStates.Released)
            {
                Root.ClearDisposeChildren();
            }

            if (Root.ChildCount > 0)
            {
                if (inputEvent.EventType != MouseInputEvent.EventTypes.Move)
                {
                    return true;
                }
                inputEvent.CanEnter = false;
            }
            return result;
        }
    }
}
