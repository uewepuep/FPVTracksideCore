using ImageServer;
using MediaFoundation;
using MediaFoundation.Alt;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using Microsoft.VisualBasic.Logging;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Implements camera device enumeration and setup via MediaFoundation.
    // When constructed with a GraphicsDevice, activates a D3D11 display path:
    //   CPU (MF decode + AVP colour-convert) → UpdateSubresource → GPU (D3D11 texture pool)
    // Without a GraphicsDevice, falls back to the rawTextures + SetData upload path.
    public class MediaFoundationDeviceFrameSource : MediaFoundationFrameSource, IHasModes, IMFSourceReaderCallback2
    {
        private Mode[] modes;

        private MFDevice device;

        // Set by the DX constructor; also settable by subclasses (CaptureFrameSourceHW).
        protected Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice;

        // Pool of D3D11-backed textures; null when graphicsDevice is null.
        protected XBuffer<Texture2DDX> dxTextures;

        public MediaFoundationDeviceFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {
        }

        // DX constructor: enables UpdateSubresource display path and D3D11 multithread protection.
        public MediaFoundationDeviceFrameSource(VideoConfig videoConfig, Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice)
            : base(videoConfig)
        {
            this.graphicsDevice = graphicsDevice;

            DeviceMultithread mt = graphicsDevice.GetSharpDXDevice().QueryInterface<DeviceMultithread>();
            mt.SetMultithreadProtected(true);
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

        // --- Reader creation: AVP only, no D3D manager ---
        // Falls back to the base (non-DX) path when graphicsDevice is null.

        protected override HResult CreateReader(IMFMediaSource pSource)
        {
            if (graphicsDevice == null)
                return base.CreateReader(pSource);

            HResult hr;
            IMFAttributes pAttributes = null;

            try
            {
                hr = MFExtern.MFCreateAttributes(out pAttributes, 4);
                MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 0);
                MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_SOURCE_READER_DISABLE_DXVA, 0);
                MFError.ThrowExceptionForHR(hr);

                // AVP inserts a software Video Processor MFT that colour-converts the
                // decoder's native YUV to the RGB32 we request in system memory.
                // No D3D manager needed — the output lands in a plain IMFMediaBuffer.
                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1);
                MFError.ThrowExceptionForHR(hr);

                if (ASync)
                {
                    hr = pAttributes.SetUnknown(MFAttributesClsid.MF_SOURCE_READER_ASYNC_CALLBACK, this);
                    MFError.ThrowExceptionForHR(hr);
                }

                hr = MFExtern.MFCreateSourceReaderFromMediaSource(pSource, pAttributes, out reader);
                MFError.ThrowExceptionForHR(hr);

                return SetupReader();
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
            }
            finally
            {
                MFHelper.SafeRelease(pAttributes);
            }

            return HResult.E_FAIL;
        }

        // --- Reader setup: ask for RGB32 output via AVP, allocate the D3D texture pool ---
        // Does NOT call base.SetupReader() — skips rawTextures allocation.
        // Falls back to base when graphicsDevice is null.

        protected override HResult SetupReader()
        {
            if (graphicsDevice == null)
                return base.SetupReader();

            HResult hr;
            IMFMediaType currentType = null;
            IMFMediaType outputType = null;

            try
            {
                readerAsync = (IMFSourceReaderAsync)reader;

                // Tell the reader we want RGB32 output. AVP inserts the Video Processor MFT
                // which colour-converts YUV→RGB32 in system memory.
                AutomaticVideoConversion = true;
                hr = SetupTransforms(out currentType, out outputType);
                MFError.ThrowExceptionForHR(hr);

                Guid currentSubType;
                hr = currentType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out currentSubType);
                MFError.ThrowExceptionForHR(hr);

                int w, h;
                hr = MFExtern.MFGetAttributeSize(outputType, MFAttributesClsid.MF_MT_FRAME_SIZE, out w, out h);
                MFError.ThrowExceptionForHR(hr);

                // Determine scanline order from the output stride attribute.
                int stride = MFExtern.MFGetAttributeUINT32(outputType, MFAttributesClsid.MF_MT_DEFAULT_STRIDE, 0);
                Direction = (stride < 0) ? Directions.TopDown : Directions.BottomUp;

                string format = MFHelper.GetFormat(currentSubType);
                Logger.VideoLog.Log(this, VideoConfig.DeviceName, "D3D path  Width: " + w + " Height: " + h + " Format: " + format + " Direction: " + Direction);

                // Pre-allocate a pool of D3D11-backed XNA textures.
                Texture2DDX[] pool = new Texture2DDX[5];
                for (int i = 0; i < pool.Length; i++)
                    pool[i] = new Texture2DDX(graphicsDevice, w, h, FrameFormat);

                dxTextures = new XBuffer<Texture2DDX>(pool);

                return HResult.S_OK;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                Logger.VideoLog.Log(this, "Type: ", MFDump.DumpAttribs(outputType));
            }
            finally
            {
                MFHelper.SafeRelease(currentType);
                MFHelper.SafeRelease(outputType);
            }

            return HResult.E_FAIL;
        }

        // --- IMFSourceReaderCallback2: required when using AVP in async mode ---

        public HResult OnTransformChange()
        {
            return HResult.S_OK;
        }

        public HResult OnStreamError(int dwStreamIndex, HResult hrStatus)
        {
            Logger.VideoLog.Log(this, "OnStreamError", "stream=" + dwStreamIndex + " hr=" + hrStatus);
            return HResult.S_OK;
        }

        // --- Frame processing: UpdateSubresource with locked buffer pointer ---
        // AVP gives us RGB32 in system memory. We lock the IMFMediaBuffer to get the
        // raw IntPtr and call UpdateSubresource directly — no byte[] intermediate,
        // one DMA transfer straight into the pooled D3D11 texture.
        // Falls back to the base SetData path when dxTextures is null.

        protected override HResult ProcessRGBSample(IMFSample sample)
        {
            XBuffer<Texture2DDX> currentDxTextures = dxTextures;
            if (currentDxTextures == null)
                return base.ProcessRGBSample(sample);

            IMFMediaBuffer buffer = null;
            try
            {
                Texture2DDX destTexture;
                if (!currentDxTextures.GetWritable(out destTexture))
                    return HResult.S_OK;

                long sampleTime;
                sample.GetSampleTime(out sampleTime);
                if (sampleTime != SampleTime)
                {
                    SampleTime = sampleTime;
                    FrameProcessNumber++;
                }

                HResult hr = sample.GetBufferByIndex(0, out buffer);
                if (MFHelper.Succeeded(hr))
                {
                    MFHelper.UpdateSubresource(graphicsDevice, buffer, destTexture, FrameWidth);

                    destTexture.FrameProcessCount = FrameProcessNumber;
                    destTexture.FrameSampleTime = sampleTime;
                    currentDxTextures.WriteOne(destTexture);
                }
            }
            finally
            {
                if (buffer != null)
                    MFHelper.SafeRelease(buffer);
            }

            return HResult.S_OK;
        }

        // --- UpdateTexture: return pooled D3D texture directly — no SetData upload ---

        public override bool UpdateTexture(Microsoft.Xna.Framework.Graphics.GraphicsDevice gd, int drawFrameCount, ref Microsoft.Xna.Framework.Graphics.Texture2D texture2D)
        {
            XBuffer<Texture2DDX> currentDxTextures = dxTextures;
            if (currentDxTextures == null)
                return base.UpdateTexture(gd, drawFrameCount, ref texture2D);

            Texture2DDX frame;
            if (currentDxTextures.ReadOne(out frame, drawFrameCount))
            {
                texture2D = frame;
                return true;
            }

            // No new frame; keep showing the last one.
            return texture2D != null;
        }

        // --- Cleanup ---

        public override void CleanUp()
        {
            XBuffer<Texture2DDX> oldDxTextures = dxTextures;
            dxTextures = null;

            base.CleanUp();

            oldDxTextures?.Dispose();
        }
    }
}
