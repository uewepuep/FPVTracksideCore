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
        public TimeSpan AnimationTime { get; protected set; }

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
                Alpha = ia.Output;
                interpolatedAlpha = null;
            }
        }

        public virtual void Update(GameTime gametime)
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
        public virtual void SetAnimationTime(TimeSpan time)
        {
            AnimationTime = time;
        }

        public virtual void SetAnimatedVisibility(bool visible)
        {
            SetAnimatedAlpha(visible ? 1 : 0);
        }

        public void ToggleAnimatedVisibility()
        {
            float alpha = Alpha;

            InterpolatedFloat ia = interpolatedAlpha;
            if (ia != null)
            {
                alpha = ia.Target;
            }

            bool visible = alpha != 0;

            SetAnimatedVisibility(!visible);
        }
    }
}
