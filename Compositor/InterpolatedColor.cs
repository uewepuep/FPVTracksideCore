using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition
{
    public class InterpolatedColor : AInterpolated<Color>
    {
        public InterpolatedColor(Color inital, Color target, TimeSpan timeToTake)
            : base(inital, target, timeToTake)
        {
        }

        protected override Color Lerp()
        {
            if (!Valid)
                return Target;

            TimeSpan taken = DateTime.Now - StartTime;
            float factor = (float)(taken.TotalSeconds / TimeToTake.TotalSeconds);

            //Clamp between 0 and 1.
            factor = Math.Min(1, Math.Max(0, factor));

            return Color.Lerp(Initial, Target, factor);
        }

    }
}
