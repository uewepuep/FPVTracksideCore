using Composition.Input;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Nodes
{

    public class CheckboxNode : ImageNode
    {
        private bool value;
        public bool Value
        {
            get
            {
                return value;
            }
            set
            {
                this.value = value;
                UpdateTexture();
            }
        }

        public event Action<bool> ValueChanged;

        public string TickFilename { get; set; }
        public string UnTickFilename { get; set; }

        public CheckboxNode(Color hoverColor)
            :this()
        {
            AddChild(new HoverNode(hoverColor));
        }

        public CheckboxNode()
        {
            TickFilename = @"img/tick.png";
            UnTickFilename = @"img/untick.png";
            SetFilename(UnTickFilename);
        }

        private void UpdateTexture()
        {
            string newFilename;
            if (value)
            {
                newFilename = TickFilename;
            }
            else
            {
                newFilename = UnTickFilename;
            }

            if (newFilename != FileName)
            {
                Texture = null;
                SetFilename(newFilename);
                RequestRedraw();
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Pressed)
            {
                Value = !Value;

                ValueChanged?.Invoke(Value);
            }

            return base.OnMouseInput(mouseInputEvent);
        }

    }
}
