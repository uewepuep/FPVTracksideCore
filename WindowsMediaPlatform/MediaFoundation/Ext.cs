using MediaFoundation;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
    public static class Ext
    {
        public static TimeSpan GetSampleTime(this IMFSample sample)
        {
            long ticks;
            sample.GetSampleTime(out ticks);
            return TimeSpan.FromTicks(ticks);
        }

        public static TimeSpan GetSampleDuration(this IMFSample sample)
        {
            long ticks;
            sample.GetSampleDuration(out ticks);
            return TimeSpan.FromTicks(ticks);
        }

        public static SharpDX.Direct3D11.Device GetSharpDXDevice(this GraphicsDevice device)
        {
            return device.Handle as SharpDX.Direct3D11.Device;
        }
    }
}
