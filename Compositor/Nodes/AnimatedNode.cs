using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class AnimatedNode : Node, IUpdateableNode
    {
        private InterpolatedRelativeRectangle interpolatedBounds;

        public TimeSpan AnimationTime { get; set; }

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

        public Rectangle TargetBounds
        {
            get
            {
                if (interpolatedBounds != null)
                {
                    return interpolatedBounds.Target;
                }
                return Bounds;
            }
        }

        public bool HasAnimation
        {
            get
            {
                return interpolatedBounds != null;
            }
        }

        public bool SnapNext { get; set; }

        public AnimatedNode()
        {
            animatingInvisiblity = false;
            AnimationTime = TimeSpan.FromSeconds(0.3f);
        }

        public virtual void Update(GameTime gameTime)
        {
            InterpolatedRelativeRectangle ib = interpolatedBounds;
            if (ib != null)
            {
                Bounds = ib.Output;

                LayoutChildren(Bounds);

                if (ib.Finished)
                {
                    Bounds = ib.Target;
                    interpolatedBounds = null;
                    RequestLayout();

                    OnAnimationFinished();
                }
            }
            else
            {
                if (animatingInvisiblity)
                {
                    animatingInvisiblity = false;
                    Visible = false;
                }
            }
        }

        public override Rectangle CalculateRelativeBounds(Rectangle parentPosition)
        {
            Rectangle bounds = base.CalculateRelativeBounds(parentPosition);

            if (animatingInvisiblity)
            {
                Rectangle tiny = new Rectangle(bounds.Center.X, bounds.Center.Y, 10, 10);

                Rectangle sizeOnly = new Rectangle(0, 0, bounds.Width, bounds.Height);

                bounds = Maths.FitBoxMaintainAspectRatio(tiny, sizeOnly, RectangleAlignment.Center, FitType.FitBoth);
            }

            return bounds;
        }

        private Rectangle oldRelativeBounds;

        public override void Layout(Rectangle parentBounds)
        {
            Rectangle newBounds = CalculateRelativeBounds(parentBounds);

            Rectangle newRelativeBounds = newBounds;
            newRelativeBounds.X -= parentBounds.X;
            newRelativeBounds.Y -= parentBounds.Y;

            Node parent = Parent;
            if (newRelativeBounds != oldRelativeBounds && parent != null)
            {
                if (!parent.IsAnimating()) // Hate
                {
                    if (interpolatedBounds == null)
                    {
                        if (SnapNext)
                        {
                            Bounds = newBounds;
                            SnapNext = false;
                        }
                        else
                        {
                            interpolatedBounds = new InterpolatedRelativeRectangle(parent, Bounds, newBounds, AnimationTime);
                        }
                    }
                    else if (interpolatedBounds.Target != newBounds)
                    {
                        Rectangle diff = new Rectangle(
                            interpolatedBounds.Target.X - interpolatedBounds.Initial.X,
                            interpolatedBounds.Target.Y - interpolatedBounds.Initial.Y,
                            interpolatedBounds.Target.Width - interpolatedBounds.Initial.Width,
                            interpolatedBounds.Target.Height - interpolatedBounds.Initial.Height);

                        float seconds = (float)interpolatedBounds.TimeToTake.TotalSeconds;

                        RectangleF velocity = new RectangleF(diff.X / seconds,
                                                             diff.Y / seconds,
                                                             diff.Width / seconds,
                                                             diff.Height / seconds);

                        interpolatedBounds.SetTarget(Bounds, newBounds);
                    }
                }
            }
            base.Layout(parentBounds);

            oldRelativeBounds = newRelativeBounds;
        }

        public virtual void OnAnimationFinished()
        {

        }

        public override void Snap()
        {
            if (interpolatedBounds != null)
            {
                Bounds = interpolatedBounds.Target;
                interpolatedBounds = null;
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
            if (interpolatedBounds != null)
            {
                return interpolatedBounds.Initial.Width != interpolatedBounds.Target.Width || 
                       interpolatedBounds.Initial.Height != interpolatedBounds.Target.Height;
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
            if (interpolatedBounds != null)
            {
                interpolatedBounds = null;
            }

            if (visible)
            {
                animatingInvisiblity = false;
                Visible = true;
            }
            else
            {
                if (Visible)
                {
                    animatingInvisiblity = true;
                    Visible = false;
                }
            }
            RequestLayout();
        }

        public override Rectangle ParentChainTargetBounds()
        {
            if (HasAnimation)
            {
                Microsoft.Xna.Framework.Rectangle target = TargetBounds;
                return target;
            }

            return base.ParentChainTargetBounds();
        }
    }
}
