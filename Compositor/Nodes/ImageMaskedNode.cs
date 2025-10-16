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

        public ImageMaskedNode()
            : base()
        {
            
        }

        public ImageMaskedNode(string imageFilename, string maskFilename)
            : base(imageFilename)
        {
            MaskFilename = maskFilename;
        }

        public override void LoadImage(Drawer id)
        {
            try
            {
                Texture2D mask = LoadMask(CompositorLayer.TextureCache, MaskFilename);
                if (mask == null)
                {
                    base.LoadImage(id);
                    return;
                }

                const bool preMultiplyAlpha = false;

                Texture2D raw = id.TextureCache.GetTextureFromFilename(FileName, Color.Transparent, ReloadFromFile, preMultiplyAlpha);
                if (SourceBounds.Width == 0 || SourceBounds.Height == 0)
                {
                    SourceBounds = new Rectangle(0, 0, raw.Width, raw.Height);
                }
                sharedTexture = false;
                ReloadFromFile = false;
                UpdateAspectRatioFromTexture();

                Texture2D profile = TextureHelper.MaskClone(id.GraphicsDevice, raw, mask);
                if (!preMultiplyAlpha)
                {
                    TextureHelper.PreMultiplyAlpha(profile);
                }

                texture = profile;
            }
            catch
            {
                base.LoadImage(id);
            }
        }


        public static Texture2D LoadMask(TextureCache textureCache, string filename)
        {
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
                return null;

            return textureCache.GetTextureFromFilename(filename, Color.White, false, false);
        }
    }
}
