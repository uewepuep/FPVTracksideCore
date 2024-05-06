using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

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


                float ox = Offset.X * frameCount % BaseBoundsF.Width;
                float oy = Offset.Y * frameCount % BaseBoundsF.Height;

                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        RectangleF bounds = new RectangleF(BaseBoundsF.X, BaseBoundsF.Y, BaseBoundsF.Width, BaseBoundsF.Height);

                        bounds.X += ox;
                        bounds.Y += oy;


                        bounds.X += x * BaseBoundsF.Width;
                        bounds.Y += y * BaseBoundsF.Height;

                        id.PushClipRectangle(BaseBoundsF.ToRectangle());
                        id.Draw(texture, SourceBounds, bounds, Tint, alpha);
                        id.PopClipRectangle();
                    }
                }

            }
        }
    }
}
