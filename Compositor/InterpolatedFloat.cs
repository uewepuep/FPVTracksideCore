using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition
{
    public class InterpolatedFloat : AInterpolated<float>
    {
        public bool SmoothStep { get; set; }

        public InterpolatedFloat(float inital, float target, TimeSpan timeToTake)
            : base(inital, target, timeToTake)
        {
            SmoothStep = false;
        }

        protected override float Lerp()
        {
            if (!Valid)
                return Target;

            TimeSpan taken = DateTime.Now - StartTime;
            float factor = (float)(taken.TotalSeconds / TimeToTake.TotalSeconds);

            //Clamp between 0 and 1.
            factor = Math.Min(1, Math.Max(0, factor));

            if (SmoothStep)
            {
                //Smooth step
                factor = MathHelper.SmoothStep(0.0f, 1.0f, factor);
            }

            float difference = Target - Initial;

            float output = Initial + (float)(difference * factor);
            return output;
        }

    }
}
