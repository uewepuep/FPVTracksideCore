using ImageServer;
using MediaFoundation;
using MediaFoundation.Alt;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using Microsoft.Xna.Framework.Graphics;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Extends MediaFoundationDeviceFrameSource with a D3D11 display path that skips
    // the SetData upload on the render side.
    //
    // Frames travel: CPU (MF decode + AVP colour-convert) → UpdateSubresource → GPU (D3D11 texture).
    // AVP gives us RGB32 in system memory; UpdateSubresource pushes that directly into
    // a pre-allocated D3D11 texture pool without an intermediate byte[] allocation.
    // The render side reads pool textures directly — no SetData upload.
    public class MediaFoundationFrameSourceDX : MediaFoundationDeviceFrameSource, IMFSourceReaderCallback2
    {
        private readonly Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice;
        private XBuffer<Texture2DDX> dxTextures;

        public MediaFoundationFrameSourceDX(VideoConfig videoConfig, Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice)
            : base(videoConfig)
        {
            this.graphicsDevice = graphicsDevice;

            // Enable D3D11 multithread protection so the MF callback thread can call
            // UpdateSubresource safely while the render thread uses the same context.
            DeviceMultithread mt = graphicsDevice.GetSharpDXDevice().QueryInterface<DeviceMultithread>();
            mt.SetMultithreadProtected(true);
        }

        // --- Reader creation: AVP only, no D3D manager ---

        protected override HResult CreateReader(IMFMediaSource pSource)
        {
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

        // --- Reader setup: ask for RGB32 output, allocate the D3D texture pool ---

        protected override HResult SetupReader()
        {
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
                // Positive stride = bottom-up (GDI/MF convention); negative = top-down.
                int stride = MFExtern.MFGetAttributeUINT32(outputType, MFAttributesClsid.MF_MT_DEFAULT_STRIDE, 0);
                Direction = (stride < 0) ? Directions.TopDown : Directions.BottomUp;

                string format = MFHelper.GetFormat(currentSubType);
                Logger.VideoLog.Log(this, VideoConfig.DeviceName, "D3D path  Width: " + w + " Height: " + h + " Format: " + format + " Direction: " + Direction);

                // Pre-allocate a pool of D3D11-backed XNA textures that match MF's output
                // format (SurfaceFormat.Bgr32 = DXGI_FORMAT_B8G8R8X8_UNORM).
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

        // --- Async callback: log status so we can diagnose pipeline failures ---

        public override HResult OnReadSample(HResult hrStatus, int dwStreamIndex, MF_SOURCE_READER_FLAG dwStreamFlags, long timestep, IMFSample sample)
        {
            if (MFHelper.Failed(hrStatus))
            {
                Logger.VideoLog.Log(this, "OnReadSample failed", "hr=" + hrStatus + " flags=" + dwStreamFlags);
            }
            else if (sample == null)
            {
                Logger.VideoLog.Log(this, "OnReadSample no sample", "flags=" + dwStreamFlags);
            }

            return base.OnReadSample(hrStatus, dwStreamIndex, dwStreamFlags, timestep, sample);
        }

        // --- Frame processing: UpdateSubresource with locked buffer pointer ---
        // AVP gives us RGB32 in system memory. We lock the IMFMediaBuffer to get the
        // raw IntPtr and call UpdateSubresource directly — no byte[] intermediate,
        // one DMA transfer straight into the pooled D3D11 texture.

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
                    IntPtr dataPtr;
                    int maxLength;
                    int currentLength;

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
