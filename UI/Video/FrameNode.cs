using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace UI.Video
{
    public class FrameNode : ImageNode, IPreProcessable
    {
        public FrameSource Source { get; private set; }
        public bool NeedsAspectRatioUpdate { get; set; }
        public int FrameNumber { get; private set; }

        private bool request;

        public FrameTextureID FrameTextureID { get { return Texture as FrameTextureID; } }

        private static Color Blank = new Color(10, 0, 10);

        public FrameNode(FrameSource s)
            :base(@"img/testpattern.png")
        {
            NeedsAspectRatioUpdate = true;

            Source = s;
            Source.References++;

            Source.OnFrameEvent += ImageArrived;

            SetAspectRatio(Source.FrameWidth, Source.FrameHeight);

            SourceBounds = new Rectangle(0, 0, Source.FrameWidth, Source.FrameHeight);
            RelativeSourceBounds = new RectangleF(0, 0, 1, 1);
            sharedTexture = true;
        }

        public override void Dispose()
        {
            Source.OnFrameEvent -= ImageArrived;
            Source.References--;

            base.Dispose();
        }

        private void ImageArrived(int id)
        {
            if (Visible)
            {
                RequestRedraw();
                request = true;
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (request)
            {
                id.PreProcess(this, true);
                request = false;
            }

            DebugTimer.DebugStartTime(this);

            float alpha = parentAlpha * Alpha;
            if (Tint.A != 255)
            {
                alpha *= Tint.A / 255.0f;
            }

            if (texture == null)
            {
                Texture2D tempTexture = id.TextureCache.GetTextureFromColor(Blank);
                id.Draw(tempTexture, new Rectangle(0,0, tempTexture.Width, tempTexture.Height), Bounds, Tint, alpha);
            }
            else
            {
                id.Draw(texture, Flip(SourceBounds), Bounds, Tint, alpha);
            }
            DebugTimer.DebugEndTime(this);

            DrawChildren(id, parentAlpha);

            if (Source != null)
            {
                Source.DrawnThisGraphicsFrame = true;
            }
        }

        public Rectangle Flip(Rectangle src)
        {
            bool flipped = false;
            bool mirrored = false;

            switch (Source.VideoConfig.FlipMirrored)
            {
                case FlipMirroreds.Flipped:
                    flipped = true;
                    mirrored = false;
                    break;
                case FlipMirroreds.Mirrored:
                    flipped = false;
                    mirrored = true;
                    break;
                case FlipMirroreds.FlippedAndMirrored:
                    flipped = true;
                    mirrored = true;
                    break;
            }

            if (flipped)
            {
                src.Y = texture.Height - src.Y;
                src.Height = -src.Height;
            }

            if (mirrored)
            {
                src.X = texture.Width - src.X;
                src.Width = -src.Width;
            }

            return src;
        }

        public virtual void PreProcess(Drawer id)
        {
            Texture2D tryTexture = texture;
            if (Source.UpdateTexture(id.GraphicsDevice, id.FrameCount, ref tryTexture))
            {
                texture = tryTexture;

                if (NeedsAspectRatioUpdate && tryTexture != null)
                {
                    NeedsAspectRatioUpdate = false;
                    UpdateAspectRatioFromTexture();
                    RequestLayout();
                }
                FrameNumber = Source.FrameCount;
            }
            texture = tryTexture;
        }
    }
}
