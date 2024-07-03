using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Input;
using Microsoft.Xna.Framework;
using Tools;

namespace Composition.Nodes
{
    public class ButtonNode : Node, IButtonNode
    {
        public ButtonNode() {}

        public event MouseInputDelegate OnClick;

        private bool pressed;

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button != MouseButtons.None && mouseInputEvent.ButtonState == ButtonStates.Pressed)
            {
                pressed = true;
                return true;
            }

            if (mouseInputEvent.Button != MouseButtons.None && mouseInputEvent.ButtonState == ButtonStates.Released && pressed)
            {
                Logger.UI.Log(this, "Click", Address, Logger.LogType.Notice);
                Click(mouseInputEvent);

                pressed = false;
                return true;
            }

            if (mouseInputEvent is MouseInputLeaveEvent)
            {
                pressed = false;
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        public void Click(MouseInputEvent mie)
        {
            OnClick?.Invoke(mie);
        }

    }

    public delegate void MouseInputDelegate(MouseInputEvent mie);
    public interface IButtonNode
    {
        event MouseInputDelegate OnClick;
    }
}
