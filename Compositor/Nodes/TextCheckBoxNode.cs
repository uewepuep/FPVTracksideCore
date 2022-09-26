using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class TextCheckBoxNode : Node
    {
        public TextNode Text { get; private set; }
        public CheckboxNode Checkbox { get; private set; }
        public bool Value { get { return Checkbox.Value; } }

        public TextCheckBoxNode(string text, Color textColor, bool checkead)
        {
            Text = new TextNode(text, textColor);
            AddChild(Text);
            
            Checkbox = new CheckboxNode();
            Checkbox.Value = checkead;
            AddChild(Checkbox);

            SetRatio(0.8f, 0.05f);
        }

        public void SetRatio(float ratio, float padding)
        {
            float half = padding / 2;
            Text.RelativeBounds = new RectangleF(0, 0, ratio - half, 1);
            Checkbox.RelativeBounds = new RectangleF(ratio + half, 0, 1 - (ratio + half), 1);
        }
    }
}
