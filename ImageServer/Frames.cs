using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace ImageServer
{
    public class RawTexture
    {
        private byte[] source;
        private int width;
        private int height;
        private int size;

        private long processCount;
        private long sampleTime;

        public RawTexture(int width, int height)
        {
            size = width * height * 4;
            source = new byte[size];

            this.width = width;
            this.height = height;
            processCount = 0;
            sampleTime = 0;
        }

        //private unsafe void MakeTestPattern()
        //{
        //    int colorIndex = 0;
        //    int boxSize = height / 5;

        //    byte[][] colorsRGB = new byte[][] {
        //        new byte[] { 235, 28, 35 },
        //        new byte[] { 34, 31, 31 },
        //        new byte[] { 255, 255, 255 },

        //        new byte[] { 25, 25, 25 },
        //        new byte[] { 193, 29, 35 },
        //    };

        //    int position;
        //    fixed (byte* pTarget = source)
        //    {
        //        for (int y = 0; y < height; y++)
        //        {
        //            for (int x = 0; x < width; x++)
        //            {
        //                position = ((y * width) + x) * 4;
                        
        //                if (x % boxSize == 0 || y % boxSize == 0)
        //                {
        //                    pTarget[position + 0] = 0;
        //                    pTarget[position + 1] = 0;
        //                    pTarget[position + 2] = 0;

        //                    if (x % boxSize == 0 || y % boxSize == 0)
        //                    {
        //                        colorIndex = (x / boxSize + y / boxSize) % colorsRGB.Length;
        //                    }
        //                }
        //                else
        //                {
        //                    pTarget[position + 0] = (byte)(colorsRGB[colorIndex][2] / 2);
        //                    pTarget[position + 1] = (byte)(colorsRGB[colorIndex][1] / 2);
        //                    pTarget[position + 2] = (byte)(colorsRGB[colorIndex][0] / 2);
        //                }
        //            }
        //        }
        //    }
        //}

        public void SetData(IntPtr bufferToCopy, long sampleTime, long processCount)
        {
            lock (source)
            {
                Marshal.Copy(bufferToCopy, source, 0, source.Length);
            }
            this.processCount = processCount;
            this.sampleTime = sampleTime;
        }

        public void SetData(char[] bufferToCopy, long sampleTime, long processCount)
        {
            lock (source)
            {
                Buffer.BlockCopy(bufferToCopy, 0, source, 0, source.Length);
            }
            this.processCount = processCount;
            this.sampleTime = sampleTime;
        }

        public bool UpdateTexture(FrameTextureSample texture)
        {
            try
            {
                if (processCount > texture.FrameProcessCount)
                {
                    System.Diagnostics.Debug.Assert(texture.Width == width);
                    System.Diagnostics.Debug.Assert(texture.Height == height);

                    lock (source)
                    {
                        texture.SetData(source, sampleTime, processCount);
                    }
                    return true;
                }
                return true;
            }
            catch (Exception e)
            {
                Tools.Logger.VideoLog.LogException(this, e);
                return false;
            }
        }
    }

    public class FrameTextureSample : Texture2D
    {
        public long FrameSampleTime { get; set; }
        public long FrameProcessCount { get; set; }

        public FrameTextureSample(GraphicsDevice graphicsDevice, int width, int height, SurfaceFormat surfaceFormat) 
            : base(graphicsDevice, width, height, false, surfaceFormat)
        {
        }

        public void SetData(byte[] data, long sampleTime, long processCount)
        {
            DebugTimer.DebugStartTime("FrameTextureSample.SetData");
            base.SetData(data);
            DebugTimer.DebugEndTime("FrameTextureSample.SetData");
            FrameSampleTime = sampleTime;
            FrameProcessCount = processCount;
        }
    }
}
