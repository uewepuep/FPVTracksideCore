using ImageServer;
using MediaFoundation;
using MediaFoundation.Alt;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using Microsoft.VisualBasic.Logging;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Implements camera device enumeration and setup via MediaFoundation.
    // When constructed with a GraphicsDevice, activates a unified D3D11 display path:
    //   DXVA decode → D3D NV12 surface → D3D11VideoProcessor (GPU blit) → D3D11 texture pool
    // Non-D3D buffers fall back to colorProcessor → UpdateSubresource.
    // Without a GraphicsDevice, falls back to the rawTextures + SetData upload path.
    public class MediaFoundationDeviceFrameSource : MediaFoundationFrameSource, IHasModes
    {
        private Mode[] modes;

        private MFDevice device;

        // Set by the DX constructor; also accessible by subclasses (CaptureFrameSourceHW).
        protected Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice;

        // Shared D3D device manager — gives MF decoders access to the XNA D3D11 device
        // so DXVA can output D3D-backed NV12 surfaces.
        protected DXGIDeviceManager dxgiManager;

        // GPU colour converter: NV12 D3D → BGRA D3D, no CPU involvement.
        protected D3D11VideoProcessor d3d11VideoProcessor;

        // Pool of D3D11-backed textures; null when graphicsDevice is null.
        protected XBuffer<Texture2DDX> dxTextures;

        // CPU NV12/YUY2 buffer pool — image processor thread copies raw bytes here,
        // render thread does all GPU work (UpdateSubresource + VP blit) in UpdateTexture.
        private XBuffer<YUVBuffer> yuvPool;
        private Format stagingFormat;
        private int lastVPBlitDrawFrame = -1;

        // Single D3D11 staging texture — written and read on the render thread only.
        private SharpDX.Direct3D11.Texture2D stagingInputTexture;

        // Plain CPU byte buffer for one NV12/YUY2 frame plus per-frame metadata.
        // Sized for max(NV12, YUY2) = width * height * 2 bytes.
        private sealed class YUVBuffer
        {
            public readonly byte[] Data;
            public int Stride;
            public long FrameProcessCount;
            public long SampleTime;

            public YUVBuffer(int width, int height) { Data = new byte[width * height * 2]; }
        }

        public MediaFoundationDeviceFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {
        }

        // DX constructor: enables unified D3D11 display path.
        public MediaFoundationDeviceFrameSource(VideoConfig videoConfig, Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice)
            : base(videoConfig)
        {
            this.graphicsDevice = graphicsDevice;

            DeviceMultithread mt = graphicsDevice.GetSharpDXDevice().QueryInterface<DeviceMultithread>();
            mt.SetMultithreadProtected(true);

            dxgiManager = new DXGIDeviceManager();
            dxgiManager.ResetDevice(graphicsDevice.GetSharpDXDevice());
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

        // --- Reader creation: D3D manager for DXVA, no AVP ---
        // DXVA decode outputs D3D-backed NV12 surfaces; D3D11VideoProcessor handles
        // colour conversion on the GPU. Falls back to the base (non-DX) path when
        // graphicsDevice is null.

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

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
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

        // --- Reader setup: NV12 output, D3D texture pool, D3D11VideoProcessor ---
        // Does NOT call base.SetupReader() — skips rawTextures allocation.
        // colorProcessor is kept as fallback for non-D3D buffers.
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

                // AutomaticVideoConversion stays false — SetupTransforms creates colorProcessor
                // as a fallback for non-D3D buffers. Primary path uses D3D11VideoProcessor.
                hr = SetupTransforms(out currentType, out outputType);
                MFError.ThrowExceptionForHR(hr);

                Guid currentSubType;
                hr = currentType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out currentSubType);
                MFError.ThrowExceptionForHR(hr);

                Guid outputSubType;
                hr = outputType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out outputSubType);
                MFError.ThrowExceptionForHR(hr);

                int stride = MFExtern.MFGetAttributeUINT32(outputType, MFAttributesClsid.MF_MT_DEFAULT_STRIDE, 0);
                if (stride != 0)
                    Direction = (stride < 0) ? Directions.TopDown : Directions.BottomUp;
                else
                    Direction = MFHelper.GetDirection(outputSubType);

                string format = MFHelper.GetFormat(currentSubType);
                Logger.VideoLog.Log(this, VideoConfig.DeviceName, "D3D path  Width: " + FrameWidth + " Height: " + FrameHeight + " Format: " + format);

                // Pre-allocate a pool of D3D11-backed XNA textures.
                Texture2DDX[] pool = new Texture2DDX[5];
                for (int i = 0; i < pool.Length; i++)
                    pool[i] = new Texture2DDX(graphicsDevice, FrameWidth, FrameHeight, FrameFormat);

                dxTextures = new XBuffer<Texture2DDX>(pool);

                d3d11VideoProcessor = new D3D11VideoProcessor(graphicsDevice.GetSharpDXDevice(), FrameWidth, FrameHeight);
                SetupStagingTexture(outputSubType);

                // Allocate rawTextures so the colorProcessor fallback path (base.ProcessRGBSample /
                // base.UpdateTexture) has a CPU buffer pool to work with.
                rawTextures = new XBuffer<RawTexture>(5, FrameWidth, FrameHeight);

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

        // --- Frame processing: D3D11VideoProcessor GPU blit, with colorProcessor fallback ---
        // If DXVA delivered a D3D-backed NV12 surface, VideoProcessorBlt converts it to
        // BGRA directly on the GPU into the dxTextures pool — no CPU readback.
        // Non-D3D buffers (software decode, exotic formats) fall back to colorProcessor.

        protected override HResult ProcessUncompressed(IMFSample sample)
        {
            D3D11VideoProcessor currentVP = d3d11VideoProcessor;
            XBuffer<Texture2DDX> currentDxTextures = dxTextures;

            if (currentVP != null && currentDxTextures != null)
            {
                IMFMediaBuffer buffer = null;
                try
                {
                    HResult hr = sample.GetBufferByIndex(0, out buffer);
                    if (MFHelper.Succeeded(hr))
                    {
                        SharpDX.Direct3D11.Texture2D inputTex;
                        int subresource;
                        if (MFHelper.TryGetD3DTexture(buffer, out inputTex, out subresource))
                        {
                            Texture2DDX destTexture;
                            if (currentDxTextures.GetWritable(out destTexture))
                            {
                                long sampleTime;
                                sample.GetSampleTime(out sampleTime);
                                if (sampleTime != SampleTime)
                                {
                                    SampleTime = sampleTime;
                                    FrameProcessNumber++;
                                }

                                currentVP.Process(inputTex, subresource, destTexture.SharpDXTexture2D);

                                destTexture.FrameProcessCount = FrameProcessNumber;
                                destTexture.FrameSampleTime = sampleTime;
                                currentDxTextures.WriteOne(destTexture);
                            }

                            inputTex.Dispose();
                            NotifyReceivedFrame();
                            return HResult.S_OK;
                        }

                        // System memory NV12/YUY2: copy raw bytes to CPU pool.
                        // All GPU work (UpdateSubresource + VP blit) is deferred to UpdateTexture
                        // on the render thread to avoid any cross-thread ImmediateContext issues.
                        XBuffer<YUVBuffer> currentYuvPool = yuvPool;
                        if (currentYuvPool != null)
                        {
                            YUVBuffer yuv;
                            if (currentYuvPool.GetWritable(out yuv))
                            {
                                long sampleTime;
                                sample.GetSampleTime(out sampleTime);
                                if (sampleTime != SampleTime)
                                {
                                    SampleTime = sampleTime;
                                    FrameProcessNumber++;
                                }

                                CopyToYUVBuffer(buffer, yuv);
                                yuv.FrameProcessCount = FrameProcessNumber;
                                yuv.SampleTime = sampleTime;
                                currentYuvPool.WriteOne(yuv);
                            }

                            NotifyReceivedFrame();
                            return HResult.S_OK;
                        }
                    }
                }
                finally
                {
                    if (buffer != null)
                        MFHelper.SafeRelease(buffer);
                }
            }

            // RGB or unsupported format — fall back to colorProcessor.
            return base.ProcessUncompressed(sample);
        }

        // --- Frame processing: UpdateSubresource with locked buffer pointer ---
        // AVP gives us RGB32 in system memory. We lock the IMFMediaBuffer to get the
        // raw IntPtr and call UpdateSubresource directly — no byte[] intermediate,
        // one DMA transfer straight into the pooled D3D11 texture.
        // Falls back to the base SetData path when dxTextures is null.

        protected override HResult ProcessRGBSample(IMFSample sample)
        {
            // Only use the dxTextures path when the VP staging path is active.
            // When falling back to colorProcessor (no staging pool), use the base rawTextures/SetData
            // path so the GPU upload happens on the render thread, not the image processor thread.
            if (yuvPool == null)
                return base.ProcessRGBSample(sample);

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

        // --- UpdateTexture: VP blit on render thread, then hand pooled texture to draw calls ---
        // Called once per draw call (up to 9x per render frame for the 3x3 grid).
        // The VP blit (staging → dxTextures) only runs on the first call per render frame,
        // identified by drawFrameCount. Subsequent calls for the same frame get the cached texture.

        public override bool UpdateTexture(Microsoft.Xna.Framework.Graphics.GraphicsDevice gd, int drawFrameCount, ref Microsoft.Xna.Framework.Graphics.Texture2D texture2D)
        {
            XBuffer<YUVBuffer> currentYuvPool = yuvPool;
            if (currentYuvPool == null)
                return base.UpdateTexture(gd, drawFrameCount, ref texture2D);

            XBuffer<Texture2DDX> currentDxTextures = dxTextures;
            if (currentDxTextures == null)
                return base.UpdateTexture(gd, drawFrameCount, ref texture2D);

            // All GPU work runs once per render frame on the render thread — first draw call only.
            if (drawFrameCount != lastVPBlitDrawFrame)
            {
                YUVBuffer yuv;
                if (currentYuvPool.ReadOne(out yuv, drawFrameCount) && yuv != null)
                {
                    Texture2DDX destTexture;
                    if (currentDxTextures.GetWritable(out destTexture))
                    {
                        try
                        {
                            UploadToStaging(yuv);
                            d3d11VideoProcessor.Process(stagingInputTexture, 0, destTexture.SharpDXTexture2D);
                            destTexture.FrameProcessCount = yuv.FrameProcessCount;
                            destTexture.FrameSampleTime = yuv.SampleTime;
                            currentDxTextures.WriteOne(destTexture);
                        }
                        catch (Exception e)
                        {
                            Logger.VideoLog.LogException(this, e);
                        }
                    }
                }
                lastVPBlitDrawFrame = drawFrameCount;
            }

            Texture2DDX frame;
            if (currentDxTextures.ReadOne(out frame, drawFrameCount))
            {
                texture2D = frame;
                return true;
            }

            // No frame yet; keep showing the last one.
            return texture2D != null;
        }

        // --- Staging texture: CPU→GPU upload for NV12/YUY2 (USB cameras) ---

        protected void SetupStagingTexture(Guid outputSubType)
        {
            Format fmt;
            if (outputSubType == MFMediaType.NV12)
                fmt = Format.NV12;
            else if (outputSubType == MFMediaType.YUY2)
                fmt = Format.YUY2;
            else
                return;

            stagingFormat = fmt;

            // Single D3D11 staging texture — only ever touched on the render thread.
            stagingInputTexture = new SharpDX.Direct3D11.Texture2D(
                graphicsDevice.GetSharpDXDevice(),
                new SharpDX.Direct3D11.Texture2DDescription
                {
                    Width = FrameWidth,
                    Height = FrameHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = fmt,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = SharpDX.Direct3D11.ResourceUsage.Default,
                    BindFlags = SharpDX.Direct3D11.BindFlags.Decoder,
                    CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
                    OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None
                });

            // CPU buffer pool — image processor thread writes here, render thread reads.
            yuvPool = new XBuffer<YUVBuffer>(5, FrameWidth, FrameHeight);

            Logger.VideoLog.Log(this, VideoConfig.DeviceName, "Created " + fmt + " staging texture + CPU YUV pool");
        }

        // Called from the image processor thread — copies raw bytes into a CPU YUVBuffer.
        private void CopyToYUVBuffer(IMFMediaBuffer buffer, YUVBuffer yuv)
        {
            IMF2DBuffer buf2D = buffer as IMF2DBuffer;
            if (buf2D != null)
            {
                IntPtr scanline0;
                int stride;
                buf2D.Lock2D(out scanline0, out stride);
                try
                {
                    int yBytes = stride * FrameHeight;
                    int uvBytes = stagingFormat == Format.NV12 ? stride * (FrameHeight / 2) : 0;
                    System.Runtime.InteropServices.Marshal.Copy(scanline0, yuv.Data, 0, yBytes + uvBytes);
                    yuv.Stride = stride;
                }
                finally { buf2D.Unlock2D(); }
            }
            else
            {
                IntPtr dataPtr;
                int maxLen, curLen;
                buffer.Lock(out dataPtr, out maxLen, out curLen);
                try
                {
                    int stride = stagingFormat == Format.NV12 ? FrameWidth : FrameWidth * 2;
                    int copyBytes = Math.Min(curLen, yuv.Data.Length);
                    System.Runtime.InteropServices.Marshal.Copy(dataPtr, yuv.Data, 0, copyBytes);
                    yuv.Stride = stride;
                }
                finally { buffer.Unlock(); }
            }
        }

        // Called from the render thread — uploads CPU bytes to the D3D11 staging texture.
        private void UploadToStaging(YUVBuffer yuv)
        {
            var context = graphicsDevice.GetSharpDXDevice().ImmediateContext;
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(yuv.Data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                context.UpdateSubresource(new SharpDX.DataBox(ptr, yuv.Stride, 0), stagingInputTexture, 0);
                if (stagingFormat == Format.NV12)
                {
                    IntPtr uvPtr = IntPtr.Add(ptr, yuv.Stride * FrameHeight);
                    context.UpdateSubresource(new SharpDX.DataBox(uvPtr, yuv.Stride, 0), stagingInputTexture, 1);
                }
            }
            finally { handle.Free(); }
        }

        // --- Cleanup ---

        public override void CleanUp()
        {
            D3D11VideoProcessor oldVP = d3d11VideoProcessor;
            d3d11VideoProcessor = null;

            XBuffer<Texture2DDX> oldDxTextures = dxTextures;
            dxTextures = null;

            XBuffer<YUVBuffer> oldYuvPool = yuvPool;
            yuvPool = null;

            SharpDX.Direct3D11.Texture2D oldStaging = stagingInputTexture;
            stagingInputTexture = null;

            base.CleanUp();

            oldVP?.Dispose();
            oldDxTextures?.Dispose();
            oldYuvPool?.Dispose();
            oldStaging?.Dispose();
        }
    }
}
