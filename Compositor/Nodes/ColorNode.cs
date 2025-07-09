using Microsoft.Xna.Framework;
using System;
using Tools;

namespace Composition.Nodes
{
    public class ColorNode : ImageNode
    {
        public Color Color
        {
            get
            {
                return color;
            }
            set
            {
                SetFilename(FileName);
                color = value;
                dirty = true;
            }
        }

        private Color color;
        private bool dirty;

        public ColorNode(Color color)
        {
            this.color = color;
            KeepAspectRatio = false;
            dirty = true;
        }

        // Loads a file first, then the color
        public ColorNode(string filename, Color color)
            :base(filename)
        {
            this.color = color;
            KeepAspectRatio = false;
            dirty = true;
        }

        public ColorNode(string filename, Rectangle sourceBounds, Color color)
            : base(filename, sourceBounds)
        {
            this.color = color;
            KeepAspectRatio = false;
            dirty = true;
        }

        public ColorNode(Tools.ToolTexture tt)
           : this(tt.TextureFilename, tt.Region, tt.XNA)
        {
        }

        public override void SetToolTexture(ToolTexture tt)
        {
            color = tt.XNA;
            base.SetToolTexture(tt);
            dirty = true;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (dirty)
            {
                Texture = null;

                if (!string.IsNullOrEmpty(FileName) && System.IO.File.Exists(FileName))
                {
                    Texture = id.TextureCache.GetTextureFromFilename(FileName, Color, false);
                    if (SourceBounds.Width == 0 || SourceBounds.Height == 0)
                    {
                        SourceBounds = new Rectangle(0, 0, Texture.Width, Texture.Height);
                    }

                    sharedTexture = true;
                    UpdateAspectRatioFromTexture();
                }

                if (texture == null)
                {
                    texture = id.TextureCache.GetTextureFromColor(Color);
                }

                sharedTexture = true;
                dirty = false;
            }
            base.Draw(id, parentAlpha);
        }
    }

}
