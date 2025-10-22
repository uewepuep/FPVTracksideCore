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

            Tools.Logger.VideoLog.LogCall(this, $"FrameNode subscribing to OnFrameEvent for source: {Source.GetType().Name} (Instance: {Source.GetHashCode()})");
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

            // Log only every 120 frames to reduce spam
            if (processNumber % 120 == 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"ImageArrived: processNumber={processNumber}, Visible={Visible}, texture={(texture != null ? "exists" : "null")}");
            }

            if (Visible)
            {
                if (CompositorLayer != null) 
                {
                    CompositorLayer.PreProcess(this, true);
                    // Log only every 120 frames to reduce spam
                    // if (processNumber % 120 == 0)
                    // {
                    //     Tools.Logger.VideoLog.LogCall(this, $"PreProcess called for frame {processNumber}");
                    // }
                }
                RequestRedraw();
                // Log only every 120 frames to reduce spam
                // if (processNumber % 120 == 0)
                // {
                //     Tools.Logger.VideoLog.LogCall(this, $"RequestRedraw called for frame {processNumber}");
                // }
            }
            else
            {
                // Log only every 120 frames to reduce spam
                // if (processNumber % 120 == 0)
                // {
                //     Tools.Logger.VideoLog.LogCall(this, $"FrameNode not visible - skipping frame {processNumber}");
                // }
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
                // Disabled: Log only every 120 frames to reduce spam
                // if (ProcessNumber % 120 == 0)
                // {
                //     Tools.Logger.VideoLog.LogCall(this, $"Draw: texture is null, drawing blank pattern. Bounds={Bounds}");
                // }
                Texture2D tempTexture = id.TextureCache.GetTextureFromColor(Blank);
                id.Draw(tempTexture, new Rectangle(0, 0, tempTexture.Width, tempTexture.Height), Bounds, Tint, alpha);
            }
            else
            {
                Rectangle sourceBounds = Flip(SourceBounds);
                // Disable draw logging to reduce spam - only log on errors
                // if (ProcessNumber % 1800 == 0)
                // {
                //     Tools.Logger.VideoLog.LogCall(this, $"Draw: Drawing texture {texture.Width}x{texture.Height}, sourceBounds={sourceBounds}, Bounds={Bounds}");
                // }
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

            // Original logic for non-ffmpeg cameras
            if (Source.VideoConfig.Flipped)
                flipped = !flipped;

            if (flipped)
                src = src.Flip(texture.Height);

            if (Source.VideoConfig.Mirrored)
                src = src.Mirror(texture.Width);

            return src;
        }

        public virtual void PreProcess(Drawer id)
        {
            Texture2D tryTexture = texture;
            if (Source != null && Source.UpdateTexture(id.GraphicsDevice, id.FrameCount, ref tryTexture))
            {
                texture = tryTexture;
                // Log only every 120 frames to reduce spam
                if (ProcessNumber % 120 == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"PreProcess: Texture updated successfully, new texture: {(texture != null ? $"{texture.Width}x{texture.Height}" : "null")}");
                }

                if (NeedsAspectRatioUpdate && texture != null)
                {
                    NeedsAspectRatioUpdate = false;
                    UpdateAspectRatioFromTexture();
                    RequestLayout();
                    // Log only every 120 frames to reduce spam
                    if (ProcessNumber % 120 == 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"PreProcess: Aspect ratio updated to {texture.Width}x{texture.Height}");
                    }
                }
            }
            else
            {
                // Log only every 120 frames to reduce spam
                if (ProcessNumber % 120 == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"PreProcess: UpdateTexture failed or Source is null");
                }
            }
            texture = tryTexture;
        }

        public void SaveImage(string filename)
        {
            bool flipped = Source.Direction == FrameSource.Directions.TopDown;

            // Special handling for ffmpeg cameras (Mac AVFoundation and Windows DirectShow) - they are upside down by default
            bool isFfmpegCamera = Source.VideoConfig.FrameWork == FrameWork.ffmpeg;
            
            if (isFfmpegCamera)
            {
                // For ffmpeg cameras (Mac/Windows): "None" should show right-side up (so flip), "Flipped" should show upside down (so don't flip) 
                if (!Source.VideoConfig.Flipped)
                    flipped = !flipped;  // When UI shows "None", flip to make it right-side up
                // When UI shows "Flipped", don't change flipped state (stays upside down)
            }
            else
            {
                // Original logic for non-ffmpeg cameras
                if (Source.VideoConfig.Flipped)
                    flipped = !flipped;
            }

            texture.SaveAs(filename, Source.VideoConfig.Mirrored, flipped);
        }
    }
}
