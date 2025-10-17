using Composition;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{
    public class FileFrameMaskedNode : FileFrameNode
    {
        public string MaskFilename { get; private set; }
        public Texture2D MaskTexture { get; private set; }
        public Rectangle MaskSourceBounds { get; private set; }
        public float SecondDrawAlpha { get; private set; }

        public FileFrameMaskedNode(FrameSource s, string maskFilename, float secondDrawAlpha) : base(s)
        {
            MaskFilename = maskFilename;
            SecondDrawAlpha = secondDrawAlpha;
        }

        public override void LoadImage(Drawer id)
        {
            base.LoadImage(id);

            if (string.IsNullOrEmpty(MaskFilename) || !File.Exists(MaskFilename))
                return;

            MaskTexture = id.TextureCache.GetTextureFromFilename(MaskFilename, Color.White, false, false);
            MaskSourceBounds = new Rectangle(0, 0, MaskTexture.Width, MaskTexture.Height);
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
