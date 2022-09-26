using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class BorderNode : Node
    {
        public Color BorderColor { get; private set; }
        private Texture2D borderTexture;

        public int Width { get; set; }

        public BorderNode(Color color)
        {
            Width = 1;
            BorderColor = color;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (borderTexture == null)
            {
                borderTexture = id.TextureCache.GetTextureFromColor(BorderColor);
            }
            base.Draw(id, parentAlpha);

            DebugTimer.DebugStartTime(this);

            if (borderTexture != null)
            {
                //top
                id.Draw(borderTexture, new Rectangle(0, 0, borderTexture.Width, borderTexture.Height), new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, Width), Color.White, Alpha);

                // left
                id.Draw(borderTexture, new Rectangle(0, 0, borderTexture.Width, borderTexture.Height), new Rectangle(Bounds.X, Bounds.Y, Width, Bounds.Height), Color.White, Alpha);

                // Right
                id.Draw(borderTexture, new Rectangle(0, 0, borderTexture.Width, borderTexture.Height), new Rectangle(Bounds.Right - Width, Bounds.Y, Width, Bounds.Height), Color.White, Alpha);

                //bottom
                id.Draw(borderTexture, new Rectangle(0, 0, borderTexture.Width, borderTexture.Height), new Rectangle(Bounds.X, Bounds.Bottom - Width, Bounds.Width, Width), Color.White, Alpha);
            }

            DebugTimer.DebugEndTime(this);
        }
    }
}
