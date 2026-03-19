using ImageServer;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Drop-in replacement for FrameTextureSample that uses UpdateSubresource
    // instead of XNA's SetData, bypassing the internal staging texture round-trip.
    // Called on the render thread so no synchronisation is needed.
    public class FrameTextureSampleDX : FrameTextureSample
    {
        private static readonly MethodInfo getTextureMethod = typeof(FrameTextureSampleDX).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(mi => mi.Name == "GetTexture");

        private SharpDX.Direct3D11.Texture2D sharpDxTexture;
        private SharpDX.Direct3D11.DeviceContext immediateContext;
        private int rowPitch;

        public FrameTextureSampleDX(GraphicsDevice graphicsDevice, int width, int height, SurfaceFormat surfaceFormat)
            : base(graphicsDevice, width, height, surfaceFormat)
        {
            immediateContext = graphicsDevice.GetSharpDXDevice().ImmediateContext;
            rowPitch = width * 4;
        }

        private SharpDX.Direct3D11.Texture2D GetSharpDXTexture()
        {
            if (sharpDxTexture == null)
            {
                object texture = getTextureMethod.Invoke(this, null);
                sharpDxTexture = texture as SharpDX.Direct3D11.Texture2D;
            }
            return sharpDxTexture;
        }

        public override void SetData(byte[] data, long sampleTime, long processCount)
        {
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                SharpDX.DataBox dataBox = new SharpDX.DataBox(handle.AddrOfPinnedObject(), rowPitch, 0);
                immediateContext.UpdateSubresource(dataBox, GetSharpDXTexture(), 0);
            }
            finally
            {
                handle.Free();
            }
            FrameSampleTime = sampleTime;
            FrameProcessCount = processCount;
        }
    }


    public class Texture2DDX : Microsoft.Xna.Framework.Graphics.Texture2D
    {
        private SharpDX.Direct3D11.Texture2D texture2D;

        public SharpDX.Direct3D11.Texture2D SharpDXTexture2D
        {
            get
            {
                if (texture2D == null)
                {
                    object texture = getTextureMethod.Invoke(this, null);
                    texture2D = texture as SharpDX.Direct3D11.Texture2D;
                }
                return texture2D;
            }
        }

        // Used by the zero-copy D3D display path to track which frame is in this texture
        public long FrameProcessCount { get; set; }
        public long FrameSampleTime { get; set; }

        private static readonly MethodInfo getTextureMethod = typeof(Texture2DDX).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(mi => mi.Name == "GetTexture");

        public Texture2DDX(GraphicsDevice graphicsDevice, int width, int height)
            : base(graphicsDevice, width, height)
        {
        }

        public Texture2DDX(GraphicsDevice graphicsDevice, int width, int height, SurfaceFormat surfaceFormat)
            : base(graphicsDevice, width, height, false, surfaceFormat)
        {
        }
    }
}
