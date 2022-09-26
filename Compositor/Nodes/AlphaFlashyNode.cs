using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Composition.Nodes
{
    public class AlphaFlashyNode : Node, IUpdateableNode
    {
        public float CyclesPerSecond { get; set; }

        public float FlashCycles { get; set; }

        private double accumulatedSeconds;
        
        public bool Flashing { get; private set; }

        public AlphaFlashyNode()
        {
            CyclesPerSecond = 2;
            FlashCycles = 3;
        }

        public void Flash()
        {
            accumulatedSeconds = 0;
            Flashing = true;
        }

        public void Stop()
        {
            Flashing = false;
            Alpha = 1;
        }

        public void Update(GameTime gameTime)
        {
            if (Flashing)
            {
                accumulatedSeconds += gameTime.ElapsedGameTime.TotalSeconds;

                double input = accumulatedSeconds * CyclesPerSecond * MathHelper.TwoPi;
                double maxValue = MathHelper.TwoPi * FlashCycles;
                double newAlpha = (Math.Cos(input) + 1) / 2;

                if (input > maxValue)
                {
                    newAlpha = 1;
                    Flashing = false;
                }

                Alpha = (float)newAlpha;
            }
        }
    }
}
