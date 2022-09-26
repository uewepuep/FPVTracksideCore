using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public ColorNode(Tools.ToolTexture tt)
           : this(tt.TextureFilename, tt.XNA)
        {
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (dirty)
            {
                Texture = null;

                if (!string.IsNullOrEmpty(FileName) && System.IO.File.Exists(FileName))
                {
                    Texture = id.TextureCache.GetTextureFromFilename(FileName, Color);
                    SourceBounds = new Rectangle(0, 0, Texture.Width, Texture.Height);
                    sharedTexture = true;
                    UpdateAspectRatioFromTexture();
                }

                if (Texture == null)
                {
                    Texture = id.TextureCache.GetTextureFromColor(Color);
                }

                sharedTexture = true;
                dirty = false;
            }
            base.Draw(id, parentAlpha);
        }
    }

}
