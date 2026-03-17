using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using Microsoft.Xna.Framework.Graphics;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using System;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Extends MediaFoundationCaptureFrameSourceHW with a D3D11 display path.
    //
    // The source reader is created with MF_SOURCE_READER_D3D_MANAGER so that
    // hardware decoders (DXVA) can deliver D3D-backed NV12 surfaces directly
    // into the NVENC sink writer — zero CPU involvement on the recording path.
    //
    // For display, colorProcessor colour-converts NV12/YUY2 → RGB32 in system
    // memory (same as the base class), but the result is pushed via
    // UpdateSubresource into a pre-allocated D3D11 texture pool instead of
    // going through Texture2D.SetData.  The render side reads pool textures
    // directly, eliminating the SetData staging-buffer upload.
    //
    // Frame path (display):
    //   DXVA/camera → colorProcessor (CPU VP) → UpdateSubresource → Texture2DDX pool → render
    // Frame path (recording):
    //   DXVA/camera → D3D NV12 sample → IMFSinkWriter → NVENC (zero-copy)
    public class MediaFoundationCaptureFrameSourceDX : MediaFoundationCaptureFrameSourceHW
    {
        // {ec822da2-e1e9-4b29-a0d8-563c719f5269}
        private static readonly Guid MF_SOURCE_READER_D3D_MANAGER =
            new Guid("ec822da2-e1e9-4b29-a0d8-563c719f5269");

        private readonly Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice;
        private DXGIDeviceManager dxgiManager;
        private XBuffer<Texture2DDX> dxTextures;

        public MediaFoundationCaptureFrameSourceDX(VideoConfig videoConfig, Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice)
            : base(videoConfig)
        {
            this.graphicsDevice = graphicsDevice;

            // Enable D3D11 multithread protection so the MF callback thread can call
            // UpdateSubresource safely while the render thread uses the same context.
            DeviceMultithread mt = graphicsDevice.GetSharpDXDevice().QueryInterface<DeviceMultithread>();
            mt.SetMultithreadProtected(true);

            dxgiManager = new DXGIDeviceManager();
            dxgiManager.ResetDevice(graphicsDevice.GetSharpDXDevice());
        }

        // --- Reader creation: same as base but with D3D manager ---

        protected override HResult CreateReader(IMFMediaSource pSource)
        {
            HResult hr;
            IMFAttributes pAttributes = null;

            try
            {
                hr = MFExtern.MFCreateAttributes(out pAttributes, 5);
                MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 0);
                MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_SOURCE_READER_DISABLE_DXVA, 0);
                MFError.ThrowExceptionForHR(hr);

                // Pass the D3D device manager so hardware decoders (DXVA) can output
                // D3D-backed NV12 surfaces.  These are fed directly to NVENC in WriteSample,
                // keeping the recording path entirely on the GPU.
                hr = pAttributes.SetUnknown(MF_SOURCE_READER_D3D_MANAGER, dxgiManager);
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

        // --- Reader setup: allocate D3D texture pool after base wires colorProcessor ---
        // base.SetupReader() calls SetupTransforms() which does:
        //   colorProcessor = new ColorProcessor(outputType, RGB32);
        //   colorProcessor.Output = ProcessRGBSample;   ← virtual dispatch → our override
        // So we just need to allocate dxTextures here; the routing is automatic.

        protected override HResult SetupReader()
        {
            HResult hr = base.SetupReader();
            if (!MFHelper.Succeeded(hr))
                return hr;

            Texture2DDX[] pool = new Texture2DDX[5];
            for (int i = 0; i < pool.Length; i++)
                pool[i] = new Texture2DDX(graphicsDevice, FrameWidth, FrameHeight, FrameFormat);

            dxTextures = new XBuffer<Texture2DDX>(pool);

            Logger.VideoLog.Log(this, VideoConfig.DeviceName, "DX capture path  Width: " + FrameWidth + " Height: " + FrameHeight);

            return HResult.S_OK;
        }

        // --- Frame processing: UpdateSubresource from CPU RGB32 into D3D texture pool ---
        // colorProcessor outputs RGB32 in system memory and calls this via its Output delegate.
        // We lock the buffer to get the raw pointer and call UpdateSubresource directly —
        // no byte[] intermediate, one DMA transfer straight into the pooled D3D11 texture.

        protected override HResult ProcessRGBSample(IMFSample sample)
        {
            var currentDxTextures = dxTextures;
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

                    hr = buffer.Lock(out dataPtr, out maxLength, out currentLength);
                    MFError.ThrowExceptionForHR(hr);
                    try
                    {
                        int rowPitch = FrameWidth * 4; // BGRA/BGRX = 4 bytes per pixel
                        var dataBox = new DataBox(dataPtr, rowPitch, 0);
                        graphicsDevice.GetSharpDXDevice().ImmediateContext.UpdateSubresource(dataBox, destTexture.SharpDXTexture2D, 0);
                    }
                    finally
                    {
                        buffer.Unlock();
                    }

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
            var currentDxTextures = dxTextures;
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
            var oldDxTextures = dxTextures;
            dxTextures = null;

            base.CleanUp();

            oldDxTextures?.Dispose();

            dxgiManager?.Dispose();
            dxgiManager = null;
        }
    }
}
