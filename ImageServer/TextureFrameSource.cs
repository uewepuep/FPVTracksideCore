using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace ImageServer
{
    public abstract class TextureFrameSource : FrameSource
    {
        protected Thread imageProcessor;
        protected bool processImages;
        private AutoResetEvent mutex;

        // Arrays for copying the data.
        protected XBuffer<RawTexture> rawTextures;
        protected Dictionary<GraphicsDevice, FrameTextureSample> textures;
        
        public bool ASync { get; set; }

        public SurfaceFormat SurfaceFormat { get; protected set; }

        public TextureFrameSource(VideoConfig videoConfig) 
            : base(videoConfig)
        {
            SurfaceFormat = SurfaceFormat.Bgr32;

            ASync = true;
        }

        public override bool Start()
        {
            if (ASync)
            {
                mutex = new AutoResetEvent(false);
            }

            textures = new Dictionary<GraphicsDevice, FrameTextureSample>();

            processImages = true;

            imageProcessor = new Thread(ProcessImages);
            imageProcessor.Name = "Image processor";

            imageProcessor.Start();

            return base.Start();
        }

        public void StopProcessing()
        {
            processImages = false;

            if (mutex != null)
            {
                mutex.Set();
            }
        }

        public override bool Stop()
        {
            StopProcessing();

            if (imageProcessor != null)
            {
                if (!imageProcessor.Join(5000))
                {
                    imageProcessor = null;
                    return false && base.Stop();
                }
                imageProcessor = null;
            }

            return base.Stop();
        }

        public override void CleanUp()
        {
            processImages = false;
            base.CleanUp();

            if (rawTextures != null)
            {
                rawTextures.Dispose();
                rawTextures = null;
            }

            if (mutex != null)
            {
                mutex.Dispose();
                mutex = null;
            }

            if (textures != null && textures.Count > 0)
            {
                foreach (FrameTextureSample fti in textures.Values)
                {
                    fti.Dispose();
                }
                textures.Clear();
            }
        }

        public void NotifyReceivedFrame()
        {
            if (mutex != null)
                mutex.Set();
        }

        private void ProcessImages()
        {
            const int timeout = 10000;
            while (processImages)
            {
                bool result = true;
                if (mutex != null)
                {
                    result = mutex.WaitOne(timeout);
                }

                if (result)
                {
                    if (!processImages)
                        break;

                    try
                    {
                        ProcessImage();
                        Connected = true;
                    }
                    catch 
                    {
                        Connected = false;
                    }
                }
                else
                {
                    if (State != States.Paused)
                    {
                        Logger.VideoLog.Log(this, "Failed to read a frame after " + timeout + "ms.");
                        Connected = false;
                    }
                }

                if (!processImages)
                    break;
            }
        }

        protected virtual void ProcessImage()
        {
            OnFrame(SampleTime, FrameProcessNumber);
        }

        public override bool UpdateTexture(GraphicsDevice graphicsDevice, int drawFrameCount, ref Texture2D texture2D)
        {
            if (rawTextures == null || textures == null)
            {
                return false;
            }

            // if the texture is disposed
            if (texture2D != null && texture2D.IsDisposed)
            {
                texture2D = null;
            }

            FrameTextureSample texture = texture2D as FrameTextureSample;
            if (texture == null)
            {
                if (!textures.TryGetValue(graphicsDevice, out texture))
                {
                    texture = new FrameTextureSample(graphicsDevice, FrameWidth, FrameHeight, SurfaceFormat);
                    textures.Add(graphicsDevice, texture);
                }
                texture2D = texture;
            }

            RawTexture frame;

            bool result = false;
            // Maybe update the texture
            if (rawTextures.ReadOne(out frame, drawFrameCount))
            {
                DebugTimer.DebugStartTime("UpdateTexture");

                result = frame.UpdateTexture(texture);

                if (result)
                {
                    texture2D = texture;
                }
                else
                {
                    foreach (Texture t in textures.Values)
                    {
                        t.Dispose();
                    }
                    // Texture is probably corrupt. 
                    textures.Clear();
                }
                DebugTimer.DebugEndTime("UpdateTexture");
            }
            return result;
        }
    }
}
