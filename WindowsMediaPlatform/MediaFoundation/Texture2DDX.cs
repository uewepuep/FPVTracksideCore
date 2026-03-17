using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
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

        private MethodInfo getTextureMethod;

        public Texture2DDX(GraphicsDevice graphicsDevice, int width, int height)
            : base(graphicsDevice, width, height)
        {
            getTextureMethod = GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(mi => mi.Name == "GetTexture");
        }

        public Texture2DDX(GraphicsDevice graphicsDevice, int width, int height, SurfaceFormat surfaceFormat)
            : base(graphicsDevice, width, height, false, surfaceFormat)
        {
            getTextureMethod = GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(mi => mi.Name == "GetTexture");
        }
    }
}
