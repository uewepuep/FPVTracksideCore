using ImageServer;
using MediaFoundation;
using MediaFoundation.Alt;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationDeviceFrameSource : MediaFoundationFrameSource, IHasModes
    {
        private Mode[] modes;

        private MFDevice device;

        public MediaFoundationDeviceFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {

        }

        public override void Dispose()
        {
            base.Dispose();
            device?.Dispose();
            device = null;
        }

        public override bool Start()
        {
            HResult hr = SetupDevice();

            if (MFHelper.Succeeded(hr))
            {
                return base.Start();
            }
            return false;
        }

        public override IEnumerable<Mode> GetModes()
        {
            if (modes != null)
            {
                return modes;
            }

            HResult hr = SetupDevice();

            if (MFHelper.Succeeded(hr))
            {
                return modes;
            }

            if (hr == HResult.MF_E_REBOOT_REQUIRED)
            {
                RebootRequired = true;
            }

            modes = null;
            return new Mode[0];
        }

        private IEnumerable<Mode> GetModesFromDevice()
        {
            Guid[] supportedSubTypes = SupportedSubTypes().ToArray();

            List<Mode> supportedModes = new List<Mode>();

            List<Mode> unsupportedModes = new List<Mode>();

            IMFMediaType[] mediaTypes = GetNativeMediaTypes();
            for (int i = 0; i < mediaTypes.Length; i++)
            {
                IMFMediaType mediaType = mediaTypes[i];

                Guid subType;
                int width;
                int height;

                mediaType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out subType);

                // Get the frame size.
                MFExtern.MFGetAttributeSize(mediaType, MFAttributesClsid.MF_MT_FRAME_SIZE, out width, out height);

                int numerator;
                int denominator;

                MFExtern.MFGetAttribute2UINT32asUINT64(mediaType, MFAttributesClsid.MF_MT_FRAME_RATE, out numerator, out denominator);

                Mode mode = new Mode();
                mode.FrameWork = FrameWork.MediaFoundation;
                mode.FrameRate = numerator / (float)denominator;
                mode.Width = width;
                mode.Height = height;
                mode.Format = MFHelper.GetFormat(subType);
                mode.Index = i;

                if (supportedSubTypes.Contains(subType))
                {
                    supportedModes.Add(mode);
                }
                else
                {
                    unsupportedModes.Add(mode);
                }

                MFHelper.SafeRelease(mediaType);
            }

            if (supportedModes.Count == 0 && unsupportedModes.Count > 0)
            {
                string modes = string.Join(", ", unsupportedModes.Select(m => m.ToString()));
                Logger.VideoLog.LogCall(this, "No supported Modes. Unsupported: " + modes);
            }

            return supportedModes;
        }

        public static IEnumerable<Guid> SupportedSubTypes()
        {
            return ColorProcessor.SupportedInputTypes().Union(DecoderProcessor.SupportedInputTypes());  
        }

        private HResult SetupDevice()
        {
            MFDevice found = MFHelper.GetVideoCaptureDeviceByPath(VideoConfig.MediaFoundationPath);
            HResult hr = HResult.E_FAIL;

            if (found != null)
            {
                hr = SetDevice(found);
            }
            return hr;
        }

        private HResult SetDevice(MFDevice pDevice)
        {
            HResult hr = HResult.S_OK;

            device = pDevice;

            IMFActivate pActivate = device.Activator;
            IMFMediaSource pSource = null;
            object o = null;

            lock (this)
            {
                try
                {
                    // Release the current device, if any.
                    CleanUp();

                    if (MFHelper.Succeeded(hr))
                    {
                        // Create the media source for the device.
                        hr = pActivate.ActivateObject(typeof(IMFMediaSource).GUID, out o);
                    }

                    if (MFHelper.Succeeded(hr))
                    {
                        pSource = (IMFMediaSource)o;
                    }
                    
                    if (MFHelper.Succeeded(hr))
                    {
                        hr = CreateReader(pSource);
                        modes = GetModesFromDevice().ToArray();
                    }
                    Connected = true;

                    if (MFHelper.Failed(hr))
                    {
                        Connected = false;
                        if (pSource != null)
                        {
                            pSource.Shutdown();

                            // NOTE: The source reader shuts down the media source
                            // by default, but we might not have gotten that far.
                        }
                        CleanUp(); 
                    }
                }
                finally
                {
                    MFHelper.SafeRelease(pSource);
                }
            }

            return hr;
        }

        protected override HResult SetupTransforms(out IMFMediaType sourceMediaType, out IMFMediaType outputMediaType)
        {
            IMFMediaType modeType = GetNativeMediaType(VideoConfig.VideoMode.Index);
            if (modeType != null)
            {
                HResult hr = reader.SetCurrentMediaType((int)MF_SOURCE_READER.FirstVideoStream, null, modeType);
                MFError.ThrowExceptionForHR(hr);
            }

            return base.SetupTransforms(out sourceMediaType, out outputMediaType);
        }
    }
}
