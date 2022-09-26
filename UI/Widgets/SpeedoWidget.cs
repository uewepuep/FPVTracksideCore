using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Widgets
{
    public class SpeedoWidget : Widget
    {
        private TextNode speedNode;
        private TextNode units;

        public SpeedoWidget()
        {
            float width = 0.8f;

            speedNode = new TextNode("", Color.White);
            speedNode.RelativeBounds = new RectangleF(0, 0, width, 1);
            speedNode.Style.Italic = true;
            speedNode.Style.Border = true;
            speedNode.Alignment = RectangleAlignment.BottomRight;
            AddChild(speedNode);
            
            units = new TextNode("km/h", Color.White);
            units.RelativeBounds = new RectangleF(width, 0.6f, 1 - width, 0.3f);
            units.Style.Italic = true;
            units.Style.Border = true;
            AddChild(units);
        }

        public void SetSpeedMS(float speedMS)
        {
            SetSpeedKPH(speedMS * 3.6f);
        }

        public void SetSpeedKPH(float speedkph)
        {
            speedkph = MathHelper.Clamp(speedkph, 0, 300);
            speedkph = (float)Math.Abs(Math.Round(speedkph, 0));

            speedNode.Text = speedkph.ToString();
        }
    }
}
