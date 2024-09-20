using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace ImageServer
{
    public class ChromaKeySample : FrameTextureSample
    {
        public ChromaKeyColor ChromaKeyColor { get; set; }
        public byte ChromaKeyLimit { get; set; }

        public ChromaKeySample(GraphicsDevice graphicsDevice, int width, int height, ChromaKeyColor chromaKeyColor, byte chromaKeyLimit) 
            : base(graphicsDevice, width, height, SurfaceFormat.Bgra32)
        {
            ChromaKeyColor = chromaKeyColor;
            ChromaKeyLimit = chromaKeyLimit;
        }

        public override void SetData(byte[] data, long sampleTime, long processCount)
        {
            DebugTimer.DebugStartTime("ChromaKeySample.SetData");

            Color[] colors = new Color[Width * Height];

            for (int i = 0; i < colors.Length; i++)
            {
                int sindex = (i * 4);
                colors[i] = new Color(data[sindex], data[sindex + 1], data[sindex + 2]);
            }

            TextureHelper.ChromaKey(colors, ChromaKeyColor, ChromaKeyLimit);

            FrameSampleTime = sampleTime;
            FrameProcessCount = processCount;

            SetData(colors);

            DebugTimer.DebugEndTime("ChromaKeySample.SetData");
        }
    }

    public class ChromaKeyCachedTextureFrameSource : CachedTextureFrameSource
    {

        public ChromaKeyColor ChromaKeyColor { get; set; }
        public byte ChromaKeyLimit { get; set; }

        public ChromaKeyCachedTextureFrameSource(GraphicsDevice graphicsDevice, VideoFrameWork videoFrameWork, string filename, ChromaKeyColor chromaKeyColor, byte chromaKeyLimit) 
            : base(graphicsDevice, videoFrameWork, filename)
        {
            ChromaKeyColor = chromaKeyColor;
            ChromaKeyLimit = chromaKeyLimit;
        }

        protected override FrameTextureSample CreateSample(GraphicsDevice graphicsDevice, int frameWidth, int frameHeight, SurfaceFormat frameFormat)
        {
            return new ChromaKeySample(graphicsDevice, frameWidth, frameHeight, ChromaKeyColor, ChromaKeyLimit);
        }

    }
}
