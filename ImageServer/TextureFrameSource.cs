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
        protected Dictionary<GraphicsDevice, FrameTextureID> textures;
        
        public bool ASync { get; set; }

        public TextureFrameSource(VideoConfig videoConfig) 
            : base(videoConfig)
        {
            ASync = true;
            FrameCount = 0;
        }

        public override bool Start()
        {
            if (ASync)
            {
                mutex = new AutoResetEvent(false);
            }

            textures = new Dictionary<GraphicsDevice, FrameTextureID>();

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
                    try
                    {
#pragma warning disable SYSLIB0006 // Type or member is obsolete
                        imageProcessor.Abort();
#pragma warning restore SYSLIB0006 // Type or member is obsolete
                    }
                    catch
                    {
                    }
                }
                imageProcessor.Join();
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
                foreach (FrameTextureID fti in textures.Values)
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
            FrameCount++;
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

                    ProcessImage();

                    Connected = true;
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
            OnFrame(FrameCount);
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

            FrameTextureID texture = texture2D as FrameTextureID;
            if (texture == null)
            {
                if (!textures.TryGetValue(graphicsDevice, out texture))
                {
                    texture = new FrameTextureID(graphicsDevice, FrameWidth, FrameHeight);
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
