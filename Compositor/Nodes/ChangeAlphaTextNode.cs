using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Nodes
{
    public class ChangeAlphaTextNode : TextNode, IUpdateableNode
    {
        private InterpolatedFloat interpolatedAlpha;

        public float HighLight { get; set; }
        public float Normal { get; set; }

        public float FadeSeconds { get; set; }

        public bool IsFullyFaded
        {
            get
            {
                if (interpolatedAlpha != null)
                {
                    return interpolatedAlpha.Finished && interpolatedAlpha.Target == Normal;
                }
                return false;
            }
        }

        public ChangeAlphaTextNode(string text, Color textColor)
            : base(text, textColor)
        {
            FadeSeconds = 6;
            Normal = 0;
            HighLight = 1;
        }

        public void SetTextAlpha(string text)
        {
            ToNormal();
            Text = text;
        }

        public void ToNormal()
        {
            interpolatedAlpha = new InterpolatedFloat(HighLight, Normal, TimeSpan.FromSeconds(FadeSeconds));
        }

        public void ToHighLight()
        {
            interpolatedAlpha = new InterpolatedFloat(Normal, HighLight, TimeSpan.FromSeconds(FadeSeconds));
        }

        public override void Snap()
        {
            if (interpolatedAlpha != null)
            {
                interpolatedAlpha.Snap();
            }
            base.Snap();
        }

        public void Update(GameTime gameTime)
        {
            if (interpolatedAlpha != null)
            {
                Alpha = interpolatedAlpha.Output;
            }
            else
            {
                Alpha = Normal;
            }
        }
    }
}
