using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Nodes;
using Tools;

namespace Composition
{
    public class InterpolatedRectangle : AInterpolated<Rectangle>
    {
        public InterpolatedRectangle(Rectangle inital, Rectangle target, TimeSpan timeToTake) 
            : base(inital, target, timeToTake)
        {
        }

        protected override Rectangle Lerp()
        {
            if (!Valid)
                return Target;

            TimeSpan taken = DateTime.Now - StartTime;
            float factor = (float)(taken.TotalSeconds / TimeToTake.TotalSeconds);


            //Slerp it
            factor = MathHelper.SmoothStep(0.0f, 1.0f, factor);

            Rectangle difference = new Rectangle(Target.X - Initial.X, Target.Y - Initial.Y, Target.Width - Initial.Width, Target.Height - Initial.Height);

            Rectangle output = Initial;
            output.X += (int)(difference.X * factor);
            output.Y += (int)(difference.Y * factor);
            output.Width += (int)(difference.Width * factor);
            output.Height += (int)(difference.Height * factor);

            return output;
        }
    }

    public class InterpolatedRectangleF : AInterpolated<RectangleF>
    {
        public InterpolatedRectangleF(RectangleF inital, RectangleF target, TimeSpan timeToTake)
            : base(inital, target, timeToTake)
        {
        }

        protected override RectangleF Lerp()
        {
            if (!Valid)
                return Target;

            TimeSpan taken = DateTime.Now - StartTime;
            float factor = (float)(taken.TotalSeconds / TimeToTake.TotalSeconds);


            //Slerp it
            factor = MathHelper.SmoothStep(0.0f, 1.0f, factor);

            RectangleF difference = new RectangleF(Target.X - Initial.X, Target.Y - Initial.Y, Target.Width - Initial.Width, Target.Height - Initial.Height);

            RectangleF output = Initial;
            output.X += (difference.X * factor);
            output.Y += (difference.Y * factor);
            output.Width += (difference.Width * factor);
            output.Height += (difference.Height * factor);

            return output;
        }

    }

    public class InterpolatedRelativeRectangle
    {
        public Node Parent { get; set; }

        public Rectangle ParentBounds {  get { return Parent.Bounds; } }

        private InterpolatedRectangle interpolatedRectangle;

        public Rectangle Initial
        {
            get
            {
                Rectangle target = interpolatedRectangle.Initial;

                target.X += ParentBounds.X;
                target.Y += ParentBounds.Y;

                return target;
            }
        }

        public Rectangle Target
        {
            get
            {
                Rectangle target = interpolatedRectangle.Target;

                target.X += ParentBounds.X;
                target.Y += ParentBounds.Y;

                return target;
            }
        }

        public Rectangle Output
        {
            get
            {
                Rectangle target = interpolatedRectangle.Output;

                target.X += ParentBounds.X;
                target.Y += ParentBounds.Y;

                return target;
            }
        }

        public bool Finished
        {
            get
            {
                return interpolatedRectangle.Finished;
            }
        }

        public InterpolatedRelativeRectangle(Node parent, Rectangle inital, Rectangle target, TimeSpan timeToTake)
        {
            Parent = parent;

            inital.X -= ParentBounds.X;
            inital.Y -= ParentBounds.Y;

            target.X -= ParentBounds.X;
            target.Y -= ParentBounds.Y;

            interpolatedRectangle = new InterpolatedRectangle(inital, target, timeToTake);
        }

        public void SetTarget(Rectangle inital, Rectangle target)
        {
            inital.X -= ParentBounds.X;
            inital.Y -= ParentBounds.Y;

            target.X -= ParentBounds.X;
            target.Y -= ParentBounds.Y;

            interpolatedRectangle.SetTarget(inital, target);
        }

        public void SetTarget(Rectangle inital, Rectangle target, TimeSpan timeToTake)
        {
            inital.X -= ParentBounds.X;
            inital.Y -= ParentBounds.Y;

            target.X -= ParentBounds.X;
            target.Y -= ParentBounds.Y;

            interpolatedRectangle.SetTarget(inital, target, timeToTake);
        }

        public void SetTimeToTake(TimeSpan timeToTake)
        {
            interpolatedRectangle.SetTarget(interpolatedRectangle.Output, interpolatedRectangle.Target, timeToTake);
        }
    }
}
