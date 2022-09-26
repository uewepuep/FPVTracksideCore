using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Nodes
{
    public class ChangeHighlightTextNode : TextNode, IUpdateableNode
    {
        private InterpolatedColor interpolatedTint;

        public Color HighLight { get; set; }
        public Color Normal { get; set; }

        public ChangeHighlightTextNode(string text, Color textColor) 
            : base(text, textColor)
        {
            Normal = Color.White;
            HighLight = Color.Yellow;
        }

        public void SetTextHighlighted(string text)
        {
            if (Text != text)
            {
                interpolatedTint = new InterpolatedColor(HighLight, Normal, TimeSpan.FromSeconds(5));
                Text = text;
            }
        }

        public void Update(GameTime gameTime)
        {
            if (interpolatedTint != null)
            {
                Tint = interpolatedTint.Output;
            }
            else
            {
                Tint = Normal;
            }
        }
    }
}
