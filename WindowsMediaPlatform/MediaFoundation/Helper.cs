using DirectShowLib.Utils;
using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.Transform;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MFHelper 
    {
        public static bool Succeeded(HResult hr) { return COMBase.Succeeded(hr); }
        public static bool Failed(HResult hr) { return COMBase.Failed(hr); }
        public static void SafeRelease(object o) { COMBase.SafeRelease(o); }


        public static FrameSource.Directions GetDirection(Guid subtype)
        {
            Guid[] bottomUp = new Guid[]
            {
                MFMediaType.ARGB32,
                MFMediaType.RGB24,
                MFMediaType.RGB32,
                MFMediaType.RGB555,
                MFMediaType.RGB565,
                MFMediaType.RGB8,

                MFMediaType.YUY2
            };

            if (bottomUp.Contains(subtype))
            {
                return FrameSource.Directions.BottomUp;
            }

            //Guid[] topDown = new Guid[]
            //{

            //    MFMediaType.AYUV,
            //    MFMediaType.I420,
            //    MFMediaType.IYUV,
            //    MFMediaType.NV11,
            //    MFMediaType.NV12,
            //    MFMediaType.UYVY,
            //    MFMediaType.v410,
            //    MFMediaType.Y216,
            //    MFMediaType.Y41P,
            //    MFMediaType.Y41T,
            //    MFMediaType.Y42T,
            //    MFMediaType.YV12
            //};

            return FrameSource.Directions.TopDown;
        }

        public static string GetFormat(Guid subtype)
        {
            foreach (var kvp in SubTypeToName())
            {
                if (kvp.Item1 == subtype)
                    return kvp.Item2;
            }

            return "Unknown subtype - " + subtype;
        }

        public static Guid GetSubType(string format)
        {
            foreach (var kvp in SubTypeToName())
            {
                if (kvp.Item2 == format)
                    return kvp.Item1;
            }

            return Guid.Empty;
        }

        private static IEnumerable<Tuple<Guid, string>> SubTypeToName()
        {
            foreach (var p in typeof(MFMediaType).GetFields())
            {
                if (p.FieldType == typeof(Guid))
                {
                    Guid v = (Guid)p.GetValue(null);
                    yield return new Tuple<Guid, string>(v, p.Name);
                }

            }

            yield return new Tuple<Guid, string>(new Guid("e436eb7d-524f-11ce-9f53-0020af0ba770"), "RGB24");
        }

        public static HResult CopyAttribute(IMFAttributes pSrc, IMFAttributes pDest, Guid key)
        {
            PropVariant var = new PropVariant();

            HResult hr = HResult.S_OK;

            hr = pSrc.GetItem(key, var);
            if (COMBase.Succeeded(hr))
            {
                hr = pDest.SetItem(key, var);
            }
            return hr;
        }

        public static void CreateD3DSample(Texture2D texture, out IMFMediaBuffer buffer)
        {
            FieldInfo type = texture.GetType().GetField("_texture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            SharpDX.Direct3D11.Texture2D value = (SharpDX.Direct3D11.Texture2D)type.GetValue(texture);
            CreateD3DSample(value, out buffer);
        }


        public static void CreateD3DSample(SharpDX.Direct3D11.Texture2D texture, out IMFMediaBuffer buffer)
        {
            HResult hr;
            hr = MFCreateDXGISurfaceBuffer2(
                typeof(SharpDX.Direct3D11.Texture2D).GUID,
                texture.NativePointer,
                0, false, out buffer
            );
            MFError.ThrowExceptionForHR(hr);
        }

        // Attempts to retrieve the underlying D3D11 texture from a D3D11-backed MF buffer.
        // Returns false (and nulls out the texture) if the buffer is not DXGI-backed.
        // Caller MUST call texture.Dispose() when done.
        public static bool TryGetD3DTexture(IMFMediaBuffer buffer, out SharpDX.Direct3D11.Texture2D texture, out int subresourceIndex)
        {
            texture = null;
            subresourceIndex = 0;

            IMFDXGIBuffer dxgiBuffer = buffer as IMFDXGIBuffer;
            if (dxgiBuffer == null)
                return false;

            IntPtr texPtr;
            HResult hr = dxgiBuffer.GetResource(typeof(SharpDX.Direct3D11.Texture2D).GUID, out texPtr);
            if (Failed(hr) || texPtr == IntPtr.Zero)
                return false;

            dxgiBuffer.GetSubresourceIndex(out subresourceIndex);

            // SharpDX constructor AddRefs, so release the ref we got from GetResource
            texture = new SharpDX.Direct3D11.Texture2D(texPtr);
            Marshal.Release(texPtr);
            return true;
        }

        // EntryPoint must be provided since we are naming the alternate
        // version MFCreateDXGISurfaceBuffer2 to separate it from the original
        [DllImport("mfplat.dll", ExactSpelling = true, EntryPoint = "MFCreateDXGISurfaceBuffer")]
        public static extern HResult MFCreateDXGISurfaceBuffer2(
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            IntPtr punkSurface,
            int uSubresourceIndex,
            [MarshalAs(UnmanagedType.Bool)] bool fBottomUpWhenLinear,
            out IMFMediaBuffer ppBuffer
        );


        public static void CreateSample(int size, out IMFSample sample, out IMFMediaBuffer buffer)
        {
            HResult hr;

            hr = MFExtern.MFCreateSample(out sample);
            MFError.ThrowExceptionForHR(hr);
            hr = MFExtern.MFCreateMemoryBuffer(size, out buffer);
            MFError.ThrowExceptionForHR(hr);

            hr = sample.AddBuffer(buffer);
            MFError.ThrowExceptionForHR(hr);
        }
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        public static void CloneSample(IMFSample sample, out IMFSample outSample)
        {
            IntPtr intPtr;
            int length;
            int current;

            IMFMediaBuffer buffer;
            IMFMediaBuffer outBuffer;

            IntPtr outPtr;

            HResult hr = sample.GetBufferByIndex(0, out buffer);
            if (Succeeded(hr))
            {
                hr = buffer.Lock(out intPtr, out length, out current);
                MFError.ThrowExceptionForHR(hr);

                CreateSample(length, out outSample, out outBuffer);

                outBuffer.Lock(out outPtr, out length, out current);

                CopyMemory(outPtr, intPtr, (uint)length);

                hr = buffer.Unlock();
                MFError.ThrowExceptionForHR(hr);

                hr = outBuffer.Unlock();
                MFError.ThrowExceptionForHR(hr);

                hr = outBuffer.SetCurrentLength(length);
                MFError.ThrowExceptionForHR(hr);

                int sampleFlags = 0;
                long llVideoTimeStamp = 0;
                long llSampleDuration = 0;
                MFError.ThrowExceptionForHR(sample.GetSampleFlags(out sampleFlags));
                MFError.ThrowExceptionForHR(sample.GetSampleTime(out llVideoTimeStamp));
                MFError.ThrowExceptionForHR(sample.GetSampleDuration(out llSampleDuration));

                MFError.ThrowExceptionForHR(outSample.SetSampleFlags(sampleFlags));
                MFError.ThrowExceptionForHR(outSample.SetSampleTime(llVideoTimeStamp));
                MFError.ThrowExceptionForHR(outSample.SetSampleDuration(llSampleDuration));

                return;
            }

            outSample = null;
        }

        public static IMFTransform CreateTransform(Guid transformCategory, MFT_EnumFlag unFlags, IMFMediaType inputMediaType, IMFMediaType outputMediaType)
        {
            MFTRegisterTypeInfo inputInfo = new MFTRegisterTypeInfo();
            inputMediaType.GetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, out inputInfo.guidMajorType);
            inputMediaType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out inputInfo.guidSubtype);

            MFTRegisterTypeInfo outputInfo = new MFTRegisterTypeInfo();
            outputMediaType.GetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, out outputInfo.guidMajorType);
            outputMediaType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out outputInfo.guidSubtype);

            return CreateTransform(transformCategory, unFlags, inputInfo, outputInfo);
        }

        public static IMFTransform CreateTransform(Guid transformCategory, MFT_EnumFlag unFlags, MFTRegisterTypeInfo inputInfo = null, MFTRegisterTypeInfo outputInfo = null)
        {
            IMFActivate[] activate_array;
            int activate_number;
            HResult hr = MFExtern.MFTEnumEx(transformCategory, unFlags, inputInfo, outputInfo, out activate_array, out activate_number);
            if (COMBase.Failed(hr))
                return null;

            if (activate_array == null)
                return null;

            IMFActivate activate = activate_array.FirstOrDefault();
            return CreateTransform(activate);
        }

        public static IMFTransform CreateTransform(IMFActivate activate)
        {
            HResult hr;
            if (activate == null)
                return null;

            object temp;
            hr = activate.ActivateObject(typeof(IMFTransform).GUID, out temp);

            if (COMBase.Failed(hr))
                return null;
            return (IMFTransform)temp;
        }

        public static IEnumerable<MFDevice> VideoCaptureDevices
        {
            get
            {
                MFDevice[] arDevices = MFDevice.GetDevicesOfCat(CLSID.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
                foreach (var ds in arDevices)
                {
                    yield return ds;
                }
            }
        }

        public static IEnumerable<MFDevice> AudioCaptureDevices
        {
            get
            {
                MFDevice[] arDevices = MFDevice.GetDevicesOfCat(CLSID.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID);
                foreach (var ds in arDevices)
                {
                    yield return ds;
                }
            }
        }

        public static MFDevice GetVideoCaptureDeviceByPath(string path)
        {
            MFDevice[] devices = VideoCaptureDevices.ToArray();
            MFDevice chosen = devices.FirstOrDefault(c => c.Path == path);

            foreach (MFDevice mf in devices)
            {
                if (mf != chosen)
                {
                    mf.Dispose();
                }
            }

            return chosen;
        }
    }

    public class MFDevice : IDisposable
    {
        private IMFActivate m_Activator;
        private string m_FriendlyName;
        private string m_SymbolicName;

        public MFDevice(IMFActivate Mon)
        {
            m_Activator = Mon;
            m_FriendlyName = null;
            m_SymbolicName = null;
        }

        ~MFDevice()
        {
            Dispose();
        }

        public IMFActivate Activator
        {
            get
            {
                return m_Activator;
            }
        }

        public string Name
        {
            get
            {
                if (m_FriendlyName == null)
                {
                    HResult hr = 0;
                    int iSize = 0;

                    hr = m_Activator.GetAllocatedString(
                        MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME,
                        out m_FriendlyName,
                        out iSize
                        );
                }

                return m_FriendlyName;
            }
        }

        /// <summary>
        /// Returns a unique identifier for a device
        /// </summary>
        public string Path
        {
            get
            {
                if (m_SymbolicName == null)
                {
                    int iSize;
                    HResult hr = m_Activator.GetAllocatedString(
                        MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
                        out m_SymbolicName,
                        out iSize
                        );
                }

                return m_SymbolicName;
            }
        }

        /// <summary>
        /// Returns an array of DsDevices of type devcat.
        /// </summary>
        /// <param name="cat">Any one of FilterCategory</param>
        public static MFDevice[] GetDevicesOfCat(Guid FilterCategory)
        {
            // Use arrayList to build the retun list since it is easily resizable
            MFDevice[] devret = null;
            IMFActivate[] ppDevices;

            //////////

            HResult hr = 0;
            IMFAttributes pAttributes = null;

            // Initialize an attribute store. We will use this to
            // specify the enumeration parameters.

            hr = MFExtern.MFCreateAttributes(out pAttributes, 1);

            // Ask for source type = video capture devices
            if (hr >= 0)
            {
                hr = pAttributes.SetGUID(
                    MFAttributesClsid.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                    FilterCategory
                    );
            }

            // Enumerate devices.
            int cDevices;
            if (hr >= 0)
            {
                hr = MFExtern.MFEnumDeviceSources(pAttributes, out ppDevices, out cDevices);

                if (hr >= 0)
                {
                    devret = new MFDevice[cDevices];

                    for (int x = 0; x < cDevices; x++)
                    {
                        devret[x] = new MFDevice(ppDevices[x]);
                    }
                }
            }

            if (pAttributes != null)
            {
                Marshal.ReleaseComObject(pAttributes);
            }

            return devret;
        }

        public override string ToString()
        {
            return Name;
        }

        public void Dispose()
        {
            if (m_Activator != null)
            {
                Marshal.ReleaseComObject(m_Activator);
                m_Activator = null;
            }
            m_FriendlyName = null;
            m_SymbolicName = null;

            GC.SuppressFinalize(this);
        }
    }
}
