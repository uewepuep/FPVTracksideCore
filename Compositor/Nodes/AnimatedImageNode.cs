using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Nodes
{
    public class AnimatedImageNode : ImageNode
    {
        public Point Offset { get; set; }

        private int frameCount;

        public AnimatedImageNode(string filename) : base(filename)
        {
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (texture == null && !string.IsNullOrEmpty(FileName))
            {
                LoadImage(id);
            }

            if (texture != null)
            {
                frameCount++;

                float alpha = parentAlpha * Alpha;
                if (Tint.A != 255)
                {
                    alpha *= Tint.A / 255.0f;
                }


                int ox = Offset.X * frameCount % BaseBounds.Width;
                int oy = Offset.Y * frameCount % BaseBounds.Height;

                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        Rectangle bounds = new Rectangle(BaseBounds.X, BaseBounds.Y, BaseBounds.Width, BaseBounds.Height);

                        bounds.X += ox;
                        bounds.Y += oy;


                        bounds.X += x * BaseBounds.Width;
                        bounds.Y += y * BaseBounds.Height;

                        id.PushClipRectangle(BaseBounds);
                        id.Draw(texture, SourceBounds, bounds, Tint, alpha);
                        id.PopClipRectangle();
                    }
                }

            }
        }
    }
}
