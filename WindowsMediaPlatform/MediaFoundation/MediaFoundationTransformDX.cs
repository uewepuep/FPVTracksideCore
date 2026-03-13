using SharpDX.Direct3D;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX.Direct3D11;
using MediaFoundation.Transform;
using MediaFoundation;
using System.Runtime.InteropServices;
using MediaFoundation.Misc;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationTransformDX : MediaFoundationTransform
    {
        private Microsoft.Xna.Framework.Graphics.GraphicsDevice device;
        private DXGIDeviceManager manager;

        public MediaFoundationTransformDX(Microsoft.Xna.Framework.Graphics.GraphicsDevice device) 
        {
            this.device = device;

            //Add multi thread protection on device
            DeviceMultithread mt = device.GetSharpDXDevice().QueryInterface<DeviceMultithread>();
            mt.SetMultithreadProtected(true);

            //Reset device
            manager = new DXGIDeviceManager();
            manager.ResetDevice(device.GetSharpDXDevice());
        }

        protected override void Initialise(Guid transformCategory, IMFMediaType inputMediaType, IMFMediaType outputMediaType, MFT_EnumFlag unFlags = MFT_EnumFlag.None, bool setOutputFirst = true)
        {
            base.Initialise(transformCategory, inputMediaType, outputMediaType, unFlags, setOutputFirst);

            HResult hr = transform.ProcessMessage(MFTMessageType.SetD3DManager, manager.NativePointer);
            MFError.ThrowExceptionForHR(hr);

            IMFAttributes attributes;
            hr = transform.GetAttributes(out attributes);
            MFError.ThrowExceptionForHR(hr);

            // Unlock the transform for async use and get event generator
            hr = attributes.SetUINT32(MFAttributesClsid.MF_TRANSFORM_ASYNC_UNLOCK, 1);
            MFError.ThrowExceptionForHR(hr);

            string name;
            hr = MFExtern.MFGetAttributeString(attributes, MFAttributesClsid.MFT_FRIENDLY_NAME_Attribute, out name);
            if (Succeeded(hr))
            {
                Logger.VideoLog.LogCall(this, name);
            }

            hr = attributes.SetUINT32(MFAttributesClsid.MF_TRANSFORM_ASYNC_UNLOCK, 0);
            MFError.ThrowExceptionForHR(hr);
        }

        protected override SampleBuffer CreateSampleBuffer()
        {
            Texture2DDX texture2DDX = new Texture2DDX(device, width, height);

            IMFMediaBuffer buffer;
            MFHelper.CreateD3DSample(texture2DDX.SharpDXTexture2D, out buffer);

            IMFSample sample;
            MFExtern.MFCreateSample(out sample);
            sample.AddBuffer(buffer);

            SampleBuffer transformedSample = new SampleBuffer();
            transformedSample.sample = sample;
            transformedSample.mediaBuffer = buffer;

            transformedSample.outputDataBuffer = new MFTOutputDataBuffer[1];

            transformedSample.outputDataBuffer[0] = new MFTOutputDataBuffer();
            transformedSample.outputDataBuffer[0].dwStreamID = 0;
            transformedSample.outputDataBuffer[0].dwStatus = 0;

            return transformedSample;
        }
    }
}
