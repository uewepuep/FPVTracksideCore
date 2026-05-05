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
        public FrameSource Source { get; protected set; }
        public bool NeedsAspectRatioUpdate { get; set; }
        public long ProcessNumber { get; private set; }
        public long SampleTime { get; private set; }

        public FrameTextureSample FrameTextureID { get { return Texture as FrameTextureSample; } }

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
            if (Source != null)
            {
                Source.OnFrameEvent -= ImageArrived;
                Source.References--;
            }
            base.Dispose();
        }

        private void ImageArrived(long sampleTime, long processNumber)
        {
            SampleTime = sampleTime;
            ProcessNumber = processNumber;

            if (Visible)
            {
                if (CompositorLayer != null) 
                {
                    CompositorLayer.PreProcess(this, true);
                }
                RequestRedraw();
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            DebugTimer.DebugStartTime(this);

            float alpha = parentAlpha * Alpha;
            if (Tint.A != 255)
            {
                alpha *= Tint.A / 255.0f;
            }

            if (texture == null)
            {
                Texture2D tempTexture = id.TextureCache.GetTextureFromColor(Blank);
                id.Draw(tempTexture, new Rectangle(0, 0, tempTexture.Width, tempTexture.Height), Bounds, Tint, alpha);
            }
            else
            {
                Rectangle sourceBounds = Flip(SourceBounds);
                id.Draw(texture, sourceBounds, Bounds, Tint, alpha);
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
            bool flipped = Source.Direction == FrameSource.Directions.TopDown;

            // Skip the user's Flipped/Mirrored toggles when the source has already baked them in
            // (e.g. ffmpeg sources apply vflip/hflip in their pipeline so recordings match preview).
            bool applyUserFlip = !Source.AppliesUserFlipMirror;

            if (applyUserFlip && Source.VideoConfig.Flipped)
                flipped = !flipped;

            if (flipped)
                src = src.Flip(texture.Height);

            if (applyUserFlip && Source.VideoConfig.Mirrored)
                src = src.Mirror(texture.Width);

            return src;
        }

        public virtual void PreProcess(Drawer id)
        {
            Texture2D tryTexture = texture;
            if (Source != null && Source.UpdateTexture(id.GraphicsDevice, id.FrameCount, ref tryTexture))
            {
                texture = tryTexture;

                if (NeedsAspectRatioUpdate && texture != null)
                {
                    NeedsAspectRatioUpdate = false;
                    UpdateAspectRatioFromTexture();
                    RequestLayout();
                }
            }
            texture = tryTexture;
        }

        public void SaveImage(string filename)
        {
            bool flipped = Source.Direction == FrameSource.Directions.TopDown;
            bool mirrored = false;

            // The texture already reflects baked-in user flip/mirror for sources like ffmpeg —
            // only apply them on top when the source hasn't done it itself.
            if (!Source.AppliesUserFlipMirror)
            {
                if (Source.VideoConfig.Flipped)
                    flipped = !flipped;
                mirrored = Source.VideoConfig.Mirrored;
            }

            texture.SaveAs(filename, mirrored, flipped);
        }
    }
}
