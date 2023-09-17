using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Composition.Nodes
{
    public class AlphaAnimatedNode : Node, IUpdateableNode
    {
        private InterpolatedFloat interpolatedAlpha;
        public TimeSpan AnimationTime { get; set; }

        public override bool IsAnimating()
        {
            return interpolatedAlpha != null;
        }

        public AlphaAnimatedNode()
        {
            AnimationTime = TimeSpan.FromSeconds(0.3f);
            interpolatedAlpha = null;
        }

        public void SetAnimatedAlpha(float newAlpha)
        {
            if (newAlpha == Alpha)
            {
                interpolatedAlpha = null;
                return;
            }

            interpolatedAlpha = new InterpolatedFloat(Alpha, newAlpha, AnimationTime);
        }

        public override void Snap()
        {
            InterpolatedFloat ia = interpolatedAlpha;
            if (ia != null)
            {
                ia.Snap();
            }
        }

        public void Update(GameTime gametime)
        {
            InterpolatedFloat ia = interpolatedAlpha;

            if (ia != null)
            {
                Alpha = ia.Output;

                if (ia.Finished)
                {
                    interpolatedAlpha = null;
                }
            }

        }

        public void SetAnimatedVisibility(bool visible)
        {
            SetAnimatedAlpha(visible ? 1 : 0);
        }
    }
}
