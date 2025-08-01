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
            // Log only every 120 frames to reduce spam
            if (FrameProcessNumber % 120 == 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"TextureFrameSource.ProcessImage: Firing OnFrame event with SampleTime={SampleTime}, FrameProcessNumber={FrameProcessNumber}");
            }
            OnFrame(SampleTime, FrameProcessNumber);
        }

        public override bool UpdateTexture(GraphicsDevice graphicsDevice, int drawFrameCount, ref Texture2D texture2D)
        {
            // Enhanced logging for video file sources to debug UI refresh issues
            bool isVideoFile = this.GetType().Name.Contains("VideoFile");
            
            if (isVideoFile && drawFrameCount % 30 == 0) // Log every 30 frames for video files
            {
                Tools.Logger.VideoLog.LogCall(this, $"VIDEO UI: UpdateTexture called - drawFrameCount: {drawFrameCount}, rawTextures: {rawTextures != null}, textures: {textures != null}");
            }
            
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
            
            // For video file sources, force texture recreation every few frames during seeks to prevent caching issues
            bool forceRecreateTexture = false;
            if (isVideoFile && drawFrameCount % 30 == 0) // Recreate texture every 30 frames for video files
            {
                forceRecreateTexture = true;
                if (texture != null)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"VIDEO UI: Force recreating texture to prevent caching (drawFrameCount: {drawFrameCount})");
                    texture.Dispose();
                    texture = null;
                    texture2D = null;
                    if (textures.ContainsKey(graphicsDevice))
                    {
                        textures.Remove(graphicsDevice);
                    }
                }
            }
            
            if (texture == null || forceRecreateTexture)
            {
                if (!textures.TryGetValue(graphicsDevice, out texture))
                {
                    texture = new FrameTextureSample(graphicsDevice, FrameWidth, FrameHeight, SurfaceFormat);
                    textures.Add(graphicsDevice, texture);
                    
                    if (isVideoFile)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"VIDEO UI: Created new texture {FrameWidth}x{FrameHeight} for video file");
                    }
                }
                texture2D = texture;
            }

            RawTexture frame;

            bool result = false;
            // Maybe update the texture
            if (rawTextures.ReadOne(out frame, drawFrameCount))
            {
                // Log more frequently for video files, less for other sources
                if ((isVideoFile && drawFrameCount % 10 == 0) || (!isVideoFile && drawFrameCount % 120 == 0))
                {
                    string prefix = isVideoFile ? "VIDEO UI" : "UI";
                    Tools.Logger.VideoLog.LogCall(this, $"{prefix}: Reading frame from rawTextures buffer for draw frame {drawFrameCount}");
                }
                DebugTimer.DebugStartTime("UpdateTexture");

                result = frame.UpdateTexture(texture);
                
                // Enhanced logging for video files to track texture update success
                if ((isVideoFile && drawFrameCount % 10 == 0) || (!isVideoFile && drawFrameCount % 120 == 0))
                {
                    string prefix = isVideoFile ? "VIDEO UI" : "UI";
                    Tools.Logger.VideoLog.LogCall(this, $"{prefix}: UpdateTexture result: {result}");
                }

                if (result)
                {
                    texture2D = texture;
                    
                    // For video file sources, force texture invalidation after seek operations to ensure UI refresh
                    if (isVideoFile)
                    {
                        // Force the texture to be marked as "changed" by updating its tag or timestamp
                        // This ensures the graphics system knows to redraw the screen
                        if (texture is FrameTextureSample frameTexture)
                        {
                            // Force graphics pipeline refresh by touching the texture properties
                            frameTexture.Tag = DateTime.Now.Ticks; // Force invalidation
                        }
                    }
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
            else
            {
                // Log when frames aren't available for video files (this could indicate the problem)
                if (isVideoFile && drawFrameCount % 60 == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"VIDEO UI: No frame available to read from rawTextures buffer (drawFrameCount: {drawFrameCount})");
                }
            }
            return result;
        }
    }
}
