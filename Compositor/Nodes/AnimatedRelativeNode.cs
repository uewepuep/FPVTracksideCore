using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class AnimatedRelativeNode : Node, IUpdateableNode
    {
        private InterpolatedRectangleF interpolatedRelativeBounds;

        public TimeSpan AnimationTime { get; protected set; }

        private bool animatingInvisiblity;

        public override bool Drawable
        {
            get
            {
                if ((animatingInvisiblity))
                    return true;
                else
                    return base.Drawable;
            }
        }

        public bool HasAnimation
        {
            get
            {
                if (interpolatedRelativeBounds != null)
                {
                    if (!interpolatedRelativeBounds.Finished)
                        return true;
                }
                return false;
            }
        }

        private RectangleF relativeBounds;
        public override RectangleF RelativeBounds
        {
            get
            {
                return relativeBounds;
            }
            set
            {
                if (relativeBounds != value)
                {
                    if (interpolatedRelativeBounds == null)
                    {
                        interpolatedRelativeBounds = new InterpolatedRectangleF(relativeBounds, value, AnimationTime);
                    }
                    else
                    {
                        interpolatedRelativeBounds.SetTarget(value, AnimationTime);

                        if (animatingInvisiblity)
                        {
                            animatingInvisiblity = false;
                            Visible = true;
                        }
                    }
                    relativeBounds = value;
                }
            }
        }

        public AnimatedRelativeNode()
        {
            animatingInvisiblity = false;
            AnimationTime = TimeSpan.FromSeconds(0.3f);
        }

        public override RectangleF CalculateRelativeBounds(RectangleF parentPosition)
        {
            RectangleF relative;

            if (interpolatedRelativeBounds == null)
            {
                relative = relativeBounds;
            }
            else
            {
                relative = interpolatedRelativeBounds.Output;
            }

            RectangleF p = new RectangleF();
            p.X = parentPosition.X + parentPosition.Width * relative.X;
            p.Y = parentPosition.Y + parentPosition.Height * relative.Y;
            p.Width = parentPosition.Width * relative.Width;
            p.Height = parentPosition.Height * relative.Height;
            return p;
        }

        public virtual void Update(GameTime gameTime)
        {
            InterpolatedRectangleF inter = interpolatedRelativeBounds;
            if (inter != null)
            {
                RequestLayout();

                LayoutChildren(BoundsF);

                if (inter.Finished)
                {
                    relativeBounds = inter.Target;
                    interpolatedRelativeBounds = null;

                    if (animatingInvisiblity)
                    {
                        animatingInvisiblity = false;
                        Visible = false;
                    }

                    RequestLayout();

                    OnAnimationFinished();
                }
            }
        }

        public virtual void OnAnimationFinished()
        {

        }

        public override void Snap()
        {
            if (interpolatedRelativeBounds != null)
            {
                interpolatedRelativeBounds = null;
                if (animatingInvisiblity)
                {
                    animatingInvisiblity = false;
                    Visible = false;
                }
                RequestLayout();
            }
            base.Snap();
        }

        public override bool IsAnimating()
        {
            if (HasAnimation)
            {
                return true;
            }

            return base.IsAnimating();
        }

        public override bool IsAnimatingSize()
        {
            if (interpolatedRelativeBounds != null)
            {
                return interpolatedRelativeBounds.Initial.Width != interpolatedRelativeBounds.Target.Width ||
                       interpolatedRelativeBounds.Initial.Height != interpolatedRelativeBounds.Target.Height;
            }

            return base.IsAnimatingSize();
        }

        public override bool IsAnimatingInvisiblity()
        {
            if (animatingInvisiblity)
                return true;

            return base.IsAnimatingInvisiblity();
        }

        public void SetAnimatedVisibility(bool visible)
        {
            if (visible)
            {
                animatingInvisiblity = false;
                Visible = true;
            }
            else
            {
                float size = 0.001f;

                float centerX = relativeBounds.CenterX - (size / 2);
                float centerY = relativeBounds.CenterY - (size / 2);

                RelativeBounds = new RectangleF(centerX, centerY, size, size);

                if (interpolatedRelativeBounds != null && Visible)
                {
                    animatingInvisiblity = true;
                }
                Visible = false;
            }
        }
        public virtual void SetAnimationTime(TimeSpan time)
        {
            AnimationTime = time;
        }
    }

    public class DebugAnimatedRelativeNode : AnimatedRelativeNode
    {
        public override RectangleF RelativeBounds
        {
            get
            {
                return base.RelativeBounds;
            }
            set
            {
                base.RelativeBounds = value;
                Logger.UI.LogCall(this, value.ToString());
            }
        }
    }
}
