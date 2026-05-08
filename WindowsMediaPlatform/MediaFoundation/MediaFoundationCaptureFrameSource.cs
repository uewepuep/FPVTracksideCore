using ImageServer;
using MediaFoundation;
using MediaFoundation.Alt;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using System;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationCaptureFrameSource : MediaFoundationDeviceFrameSource, ICaptureFrameSource
    {
        private MFSinkWriterRecorder recorder;
        private Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice;
        private DXGIDeviceManager dxgiManager;

        protected IMFMediaType encoderOutputType;
        protected bool passthroughCompressed;

        public string Filename => recorder.Filename;
        public FrameTime[] FrameTimes => recorder.FrameTimes;
        public bool RecordNextFrameTime { get => recorder.RecordNextFrameTime; set => recorder.RecordNextFrameTime = value; }
        public bool ManualRecording { get => recorder.ManualRecording; set => recorder.ManualRecording = value; }
        public bool Finalising => recorder.Finalising;
        public MFSinkWriterRecorder.FileFormats FileFormat { get => recorder.FileFormat; set => recorder.FileFormat = value; }

        public MediaFoundationCaptureFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {
            recorder = new MFSinkWriterRecorder(videoConfig);
            passthroughCompressed = false;
        }

        public MediaFoundationCaptureFrameSource(VideoConfig videoConfig, Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice)
            : this(videoConfig)
        {
            if (!videoConfig.HardwareAcceleration)
                return;

            this.graphicsDevice = graphicsDevice;

            // Enable D3D11 multithread protection so the MF callback thread can call
            // UpdateSubresource safely while the render thread uses the same context.
            SharpDX.Direct3D.DeviceMultithread mt = graphicsDevice.GetSharpDXDevice().QueryInterface<SharpDX.Direct3D.DeviceMultithread>();
            mt.SetMultithreadProtected(true);

            dxgiManager = new DXGIDeviceManager();
            dxgiManager.ResetDevice(graphicsDevice.GetSharpDXDevice());
        }

        // --- Reader creation: with D3D manager when available for hardware decode ---

        protected override HResult CreateReader(IMFMediaSource pSource)
        {
            if (graphicsDevice == null || !VideoConfig.HardwareAcceleration)
                return base.CreateReader(pSource);

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
                hr = pAttributes.SetUnknown(MFHelper.MF_SOURCE_READER_D3D_MANAGER, dxgiManager);
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

        // --- Recording ---

        public virtual void StartRecording(string filename)
        {
            if (passthroughCompressed)
                recorder.StartRecording(filename, GetCurrentMediaType(), true);
            else
                recorder.StartRecording(filename, encoderOutputType);

            Recording = recorder.Recording;
        }

        public void StopRecording()
        {
            recorder.StopRecording();
            Recording = recorder.Recording;
        }

        public override void CleanUp()
        {
            Logger.VideoLog.LogCall(this);

            dxgiManager?.Dispose();
            dxgiManager = null;

            MFHelper.SafeRelease(encoderOutputType);
            encoderOutputType = null;

            recorder.CleanUp();
            Recording = recorder.Recording;

            base.CleanUp();
        }

        protected override HResult ProcessRaw(IMFSample sample)
        {
            HResult hr = base.ProcessRaw(sample);
            if (MFHelper.Succeeded(hr) && passthroughCompressed)
                recorder.WriteSample(sample);
            return hr;
        }

        protected override HResult ProcessUncompressed(IMFSample sample)
        {
            HResult hr = base.ProcessUncompressed(sample);
            if (MFHelper.Succeeded(hr) && !passthroughCompressed)
                recorder.WriteSample(sample);
            return hr;
        }

        protected override void ProcessImage()
        {
            recorder.ProcessEncoderOutput();
            base.ProcessImage();
        }

        protected override HResult SetupTransforms(out IMFMediaType sourceMediaType, out IMFMediaType outputMediaType)
        {
            HResult hr = base.SetupTransforms(out sourceMediaType, out outputMediaType);
            if (!MFHelper.Succeeded(hr))
                return hr;

            int height = VideoConfig.RecordResolution;
            if (decoderProcessor != null)
            {
                Guid subType;
                hr = sourceMediaType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out subType);
                MFError.ThrowExceptionForHR(hr);

                int frameWidth, frameHeight;
                hr = MFExtern.MFGetAttributeSize(outputMediaType, MFAttributesClsid.MF_MT_FRAME_SIZE, out frameWidth, out frameHeight);
                MFError.ThrowExceptionForHR(hr);

                if (subType == MFMediaType.H264 && frameHeight == height)
                {
                    passthroughCompressed = true;
                    return HResult.S_OK;
                }
            }

            passthroughCompressed = false;
            encoderOutputType = outputMediaType;

            return hr;
        }
    }
}
