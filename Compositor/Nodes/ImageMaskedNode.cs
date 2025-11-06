using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class ImageMaskedNode : ImageNode
    {
        public string MaskFilename { get; private set; }
        public Texture2D MaskTexture { get; private set; }
        public Rectangle MaskSourceBounds { get; private set; }

        public float SecondDrawAlpha { get; private set; }

        public ImageMaskedNode(string imageFilename, string maskFilename, float secondDrawAlpha)
            : base(imageFilename)
        {
            SecondDrawAlpha = secondDrawAlpha;
            MaskFilename = maskFilename;
        }


        public override void LoadImage(Drawer id)
        {
            base.LoadImage(id);

            if (string.IsNullOrEmpty(MaskFilename) || !File.Exists(MaskFilename))
                return;

            MaskTexture = id.TextureCache.GetTextureFromFilename(MaskFilename, Color.White, false, false);
            MaskSourceBounds = new Rectangle(0,0, MaskTexture.Width, MaskTexture.Height);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            DebugTimer.DebugStartTime(this);

            if ((texture == null || ReloadFromFile) && !string.IsNullOrEmpty(FileName))
            {
                LoadImage(id);
            }

            if (MaskTexture == null)
            {
                base.Draw(id, parentAlpha);
                return;
            }

            if (texture != null)
            {
                try
                {
                    id.DrawMasked(Texture, SourceBounds, MaskTexture, MaskSourceBounds, Bounds, Tint);

                    if (SecondDrawAlpha > 0)
                    {
                        // Draw it again but really alha'd
                        float alpha = parentAlpha * SecondDrawAlpha * Alpha;
                        id.Draw(Texture, SourceBounds, Bounds, Tint, alpha);
                    }
                }
                catch
                {
                    if (!sharedTexture && texture != null)
                    {
                        texture.Dispose();
                    }
                    Texture = null;
                }
            }

            DebugTimer.DebugEndTime(this);
        }
    }
}
