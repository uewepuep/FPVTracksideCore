using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class GamePointsNode : Node
    {
        private TextNode number;
        private TextNode desc;
        private RaceManager raceManager;

        private int points;
        public int Points
        {
            get
            {
                return points;
            }
            set
            {
                points = value;
                number.Text = points.ToString();

                number.Visible = true;
                desc.Visible = true;
            }
        }

        public event Action OnCtrlClick;

        public GamePointsNode(Color textColor) 
        {
            number = new TextNode("", textColor);
            number.RelativeBounds = new RectangleF(0, 0, 0.8f, 1);
            number.Alignment = RectangleAlignment.BottomRight;
            number.Style.Bold = true;
            number.Style.Border = true;
            number.Visible = false;
            AddChild(number);

            desc = new TextNode("pts", textColor);
            desc.Alignment = RectangleAlignment.BottomLeft;
            desc.RelativeBounds = new RectangleF(number.RelativeBounds.Right, 0.7f, 1 - number.RelativeBounds.Right, 0.2f);
            desc.Visible = false;
            desc.Style.Bold = true;
            desc.Style.Border = true;
            AddChild(desc);
        }

        public void Clear()
        {
            number.Visible = false;
            desc.Visible = false;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (CompositorLayer.InputEventFactory.AreControlKeysDown() && mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                OnCtrlClick?.Invoke();
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }
}
