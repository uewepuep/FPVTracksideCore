using MediaFoundation;
using MediaFoundation.Misc;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
    // COM interface for IMFDXGIBuffer — exposes the underlying D3D11 texture
    // from a D3D11-backed IMFMediaBuffer produced by a DXVA-enabled source reader.
    [ComImport, System.Security.SuppressUnmanagedCodeSecurity,
     Guid("e7174cfa-1c9e-48b1-8866-626226bfc258"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFDXGIBuffer
    {
        [PreserveSig] HResult GetResource([In, MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppvObject);
        [PreserveSig] HResult GetSubresourceIndex(out int puSubresource);
        [PreserveSig] HResult GetUnknown([In, MarshalAs(UnmanagedType.LPStruct)] Guid guid, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppvObject);
        [PreserveSig] HResult SetUnknown([In, MarshalAs(UnmanagedType.LPStruct)] Guid guid, [In] object punkData);
    }

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
