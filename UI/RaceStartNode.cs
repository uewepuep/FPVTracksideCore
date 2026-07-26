using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;

namespace UI
{
    public class RaceStartNode : AlphaAnimatedNode
    {
        private DateTime expires;
        private bool timedOut;

        public RaceStartNode()
        {
            Alpha = 0;
            timedOut = true;

            ColorNode colorNode = new ColorNode(Theme.Current.RaceStartGraphic);
            AddChild(colorNode);
        }


        public void Show()
        {
            Show(TimeSpan.FromSeconds(2));
        }

        public void Show(TimeSpan showFor)
        {
            expires = DateTime.Now + showFor;
            timedOut = false;

            // Snap straight to visible - only the fade-out should animate.
            SetAnimatedAlpha(1);
            Snap();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!timedOut && DateTime.Now > expires)
            {
                timedOut = true;
                SetAnimatedAlpha(0);
            }
        }
    }
}
