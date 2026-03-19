using ImageServer;
using MediaFoundation;
using MediaFoundation.Alt;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using MediaFoundation.Transform;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationCaptureFrameSource : MediaFoundationDeviceFrameSource, ICaptureFrameSource
    {
        protected IMFSinkWriter writer;
        protected int sink_stream;

        protected MediaFoundationTransform encoder;

        private Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice;
        private DXGIDeviceManager dxgiManager;

        public string Filename { get; protected set; }

        public FrameTime[] FrameTimes
        {
            get
            {
                lock (frameTimes)
                {
                    return frameTimes.ToArray();
                }
            }
        }

        public bool RecordNextFrameTime { get; set; }

        protected List<FrameTime> frameTimes;

        protected int receivedFrameCount;

        protected TimeSpan receivedDuration;
        protected TimeSpan recordingTargetLength;

        protected bool flushing;

        protected TimeSpan firstSampleTime;

        protected object writerLocker;
        protected bool hasBegun;

        protected IMFMediaType encoderOutputType;

        public enum FileFormats
        {
            WMV,
            mp4
        }

        public FileFormats FileFormat { get; set; }

        protected bool passthroughCompressed;

        public bool ManualRecording { get; set; }

        public bool Finalising
        {
            get
            {
                return flushing || encoder != null;
            }
        }

        public MediaFoundationCaptureFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {
            FileFormat = FileFormats.mp4;
            writerLocker = new object();
            frameTimes = new List<FrameTime>();
            passthroughCompressed = false;
        }

        public MediaFoundationCaptureFrameSource(VideoConfig videoConfig, Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice)
            : this(videoConfig)
        {
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
            if (graphicsDevice == null)
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

        // --- Recording setup: tries NVENC first, falls back to software ---

        public virtual void StartRecording(string filename)
        {
            // Passthrough (camera already outputs H264) and no encoder type go straight to software.
            if (encoderOutputType == null || passthroughCompressed)
            {
                StartRecordingSoftware(filename);
                return;
            }

            IMFMediaType h264Type = null;
            IMFAttributes attributes = null;

            try
            {
                Logger.VideoLog.LogCall(this);

                Filename = filename + "." + FileFormat.ToString().ToLower();
                hasBegun = false;

                if (Recording)
                    StopRecording();

                flushing = false;
                receivedDuration = TimeSpan.Zero;
                recordingTargetLength = TimeSpan.Zero;
                firstSampleTime = TimeSpan.Zero;
                receivedFrameCount = 0;

                if (File.Exists(Filename))
                    File.Delete(Filename);

                HResult hr;

                lock (writerLocker)
                {
                    int targetHeight = VideoConfig.RecordResolution;
                    int targetWidth = GetWidth(encoderOutputType, targetHeight);
                    int frameRate = VideoConfig.RecordFrameRate;

                    // Build the H264 output type that the sink writer will produce.
                    hr = MFExtern.MFCreateMediaType(out h264Type);
                    MFError.ThrowExceptionForHR(hr);

                    hr = h264Type.SetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, MFMediaType.Video);
                    MFError.ThrowExceptionForHR(hr);

                    hr = h264Type.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, MFMediaType.H264);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeSize(h264Type, MFAttributesClsid.MF_MT_FRAME_SIZE, targetWidth, targetHeight);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeRatio(h264Type, MFAttributesClsid.MF_MT_FRAME_RATE, frameRate, 1);
                    MFError.ThrowExceptionForHR(hr);

                    hr = h264Type.SetUINT32(MFAttributesClsid.MF_MT_INTERLACE_MODE, 2); // Progressive
                    MFError.ThrowExceptionForHR(hr);

                    MFHelper.CopyAttribute(encoderOutputType, h264Type, MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO);

                    int bitRate = H264Encoder.GetBitRate(targetWidth, targetHeight, frameRate);
                    hr = h264Type.SetUINT32(MFAttributesClsid.MF_MT_AVG_BITRATE, bitRate);
                    MFError.ThrowExceptionForHR(hr);

                    hr = h264Type.SetUINT32(MFAttributesClsid.MF_MT_MPEG2_PROFILE, 77); // Main
                    MFError.ThrowExceptionForHR(hr);

                    // Create sink writer. Hardware transforms enabled so it picks up NVENC/QSV/AMF.
                    hr = MFExtern.MFCreateAttributes(out attributes, 2);
                    MFError.ThrowExceptionForHR(hr);

                    hr = attributes.SetUINT32(MFAttributesClsid.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                    MFError.ThrowExceptionForHR(hr);

                    hr = attributes.SetUINT32(MFAttributesClsid.MF_LOW_LATENCY, 1);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFCreateSinkWriterFromURL(Filename, null, attributes, out writer);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.AddStream(h264Type, out sink_stream);
                    MFError.ThrowExceptionForHR(hr);

                    // Feed raw NV12 — the sink writer inserts the hardware encoder internally.
                    hr = writer.SetInputMediaType(sink_stream, encoderOutputType, null);
                    MFError.ThrowExceptionForHR(hr);

                    MFHelper.LogSinkWriterTransforms(this, writer, sink_stream);

                    Recording = true;
                    RecordNextFrameTime = true;
                    lock (frameTimes)
                    {
                        frameTimes.Clear();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                Logger.VideoLog.Log(this, "Hardware recording setup failed, falling back to software");
                StopRecording();
                StartRecordingSoftware(filename);
            }
            finally
            {
                MFHelper.SafeRelease(h264Type);
                MFHelper.SafeRelease(attributes);
            }
        }

        private void StartRecordingSoftware(string filename)
        {
            IMFMediaType format = null;
            try
            {
                Logger.VideoLog.LogCall(this);

                Filename = filename + "." + FileFormat.ToString().ToLower();
                hasBegun = false;

                if (Recording)
                {
                    StopRecording();
                }

                flushing = false;
                receivedDuration = TimeSpan.Zero;
                recordingTargetLength = TimeSpan.Zero;

                firstSampleTime = TimeSpan.Zero;
                receivedFrameCount = 0;

                if (File.Exists(Filename))
                {
                    File.Delete(Filename);
                }

                HResult hr;

                lock (writerLocker)
                {
                    if (encoderOutputType != null && !passthroughCompressed)
                    {
                        CreateEncoder();
                    }

                    if (encoder != null)
                    {
                        format = encoder.GetOutputCurrentType();
                    }

                    if (passthroughCompressed)
                    {
                        format = GetCurrentMediaType();

                        Guid subType;
                        hr = format.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out subType);
                        MFError.ThrowExceptionForHR(hr);

                        if (subType != MFMediaType.H264)
                        {
                            throw new Exception("h264 passthrough type wrong.");
                        }
                    }

                    if (format != null)
                    {
                        IMFAttributes attributes = null;

                        hr = MFExtern.MFCreateAttributes(out attributes, 1);
                        MFError.ThrowExceptionForHR(hr);

                        hr = attributes.SetUINT32(MFAttributesClsid.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                        MFError.ThrowExceptionForHR(hr);

                        hr = attributes.SetUINT32(MFAttributesClsid.MF_LOW_LATENCY, 1);
                        MFError.ThrowExceptionForHR(hr);

                        hr = MFExtern.MFCreateSinkWriterFromURL(Filename, null, attributes, out writer);
                        MFError.ThrowExceptionForHR(hr);

                        hr = writer.AddStream(format, out sink_stream);
                        MFError.ThrowExceptionForHR(hr);

                        hr = writer.SetInputMediaType(sink_stream, format, null);
                        MFError.ThrowExceptionForHR(hr);

                        MFHelper.LogSinkWriterTransforms(this, writer, sink_stream);

                        Recording = true;
                        RecordNextFrameTime = true;
                        lock (frameTimes)
                        {
                            frameTimes.Clear();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                StopRecording();
            }
            finally
            {
                MFHelper.SafeRelease(format);
            }
        }

        public void StopRecording()
        {
            if (Recording)
            {
                Recording = false;
                recordingTargetLength = receivedDuration;
                Logger.VideoLog.LogCall(this, "Length " + receivedDuration);
                flushing = true;
            }
        }

        private void FlushFinalize()
        {
            Logger.VideoLog.LogCall(this);

            try
            {
                HResult hr;

                if (encoder != null)
                {
                    hr = encoder.Flush();
                    MFError.ThrowExceptionForHR(hr);
                }

                lock (writerLocker)
                {
                    if (writer != null)
                    {
                        hr = writer.Flush(0);
                        MFError.ThrowExceptionForHR(hr);

                        hr = writer.Finalize_();
                        MFError.ThrowExceptionForHR(hr);
                        writer = null;
                    }
                }

                encoder?.Dispose();
                encoder = null;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
            }
            finally
            {
                MFHelper.SafeRelease(writer);
                writer = null;
                flushing = false;
            }
        }

        public override void CleanUp()
        {
            Logger.VideoLog.LogCall(this);

            dxgiManager?.Dispose();
            dxgiManager = null;

            if (Recording)
            {
                StopRecording();
            }

            if (encoder != null)
            {
                encoder.Dispose();
                encoder = null;
            }

            MFHelper.SafeRelease(encoderOutputType);
            encoderOutputType = null;

            base.CleanUp();
        }

        protected override HResult ProcessRaw(IMFSample sample)
        {
            HResult hr = base.ProcessRaw(sample);
            if ((Recording || flushing) && MFHelper.Succeeded(hr) && passthroughCompressed)
            {
                TimeSpan newSampleTime = CalculateSampleTime(sample);
                sample.SetSampleTime(newSampleTime.Ticks);

                WriteSample(sample);
            }

            return hr;
        }

        protected override HResult ProcessUncompressed(IMFSample sample)
        {
            HResult hr = base.ProcessUncompressed(sample);

            if ((Recording || flushing) && MFHelper.Succeeded(hr))
            {
                TimeSpan newSampleTime = CalculateSampleTime(sample);
                sample.SetSampleTime(newSampleTime.Ticks);

                if (writer != null)
                {
                    WriteSample(sample);
                }
                else if (encoder != null)
                {
                    encoder.ProcessInput(sample);
                }
            }

            return hr;
        }

        protected TimeSpan CalculateSampleTime(IMFSample sample)
        {
            receivedFrameCount++;

            TimeSpan sampleTime = sample.GetSampleTime();

            if (receivedFrameCount == 1)
            {
                firstSampleTime = sampleTime;
            }

            receivedDuration = sampleTime - firstSampleTime;

            TimeSpan newSampleTime = sampleTime - firstSampleTime;

            if (RecordNextFrameTime)
            {
                lock (frameTimes)
                {
                    frameTimes.Add(new FrameTime() { Frame = receivedFrameCount, Time = DateTime.Now, Seconds = newSampleTime.TotalSeconds });
                }

                RecordNextFrameTime = false;
            }

            long dur;
            sample.GetSampleDuration(out dur);

            return newSampleTime;
        }

        protected override void ProcessImage()
        {
            if (encoder != null)
            {
                try
                {
                    HResult hr = HResult.S_OK;
                    hr = encoder.ProcessOutput();
                    MFError.ThrowExceptionForHR(hr);
                }
                catch (Exception ex)
                {
                    Logger.VideoLog.LogException(this, ex);
                    StopRecording();
                }
            }

            base.ProcessImage();
        }

        protected HResult WriteSample(IMFSample sample)
        {
            HResult hr = HResult.S_OK;

            lock (writerLocker)
            {
                if (writer != null && sample != null)
                {
                    if (!hasBegun)
                    {
                        hr = writer.BeginWriting();
                        MFError.ThrowExceptionForHR(hr);
                        hasBegun = true;
                    }

                    TimeSpan sampleTime = sample.GetSampleTime();

                    hr = writer.WriteSample(0, sample);
                    MFError.ThrowExceptionForHR(hr);

                    if (flushing && sampleTime >= recordingTargetLength)
                    {
                        Logger.VideoLog.LogCall(this, "Final Length " + sampleTime);

                        flushing = false;
                        FlushFinalize();
                    }
                }
            }
            return hr;
        }

        public static int GetWidth(IMFMediaType outputMediaType, int targetHeight)
        {
            int frameWidth, frameHeight;

            HResult hr = MFExtern.MFGetAttributeSize(outputMediaType, MFAttributesClsid.MF_MT_FRAME_SIZE, out frameWidth, out frameHeight);
            MFError.ThrowExceptionForHR(hr);

            double[] validAspects = new double[]
            {
                4.0 / 3.0,
                16.0 / 9.0
            };

            double thisAspect = frameWidth / (double)frameHeight;
            thisAspect = validAspects.OrderBy(n => Math.Abs(n - thisAspect)).First();

            int width = (int)(thisAspect * targetHeight);

            // hack to to do rounding
            width /= 10;
            width *= 10;

            return width;
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

        protected virtual void CreateEncoder()
        {
            int height = VideoConfig.RecordResolution;
            int width = GetWidth(encoderOutputType, height);
            int frameRate = VideoConfig.RecordFrameRate;

            switch (FileFormat)
            {
                case FileFormats.WMV:
                    encoder = new WMVEncoder(encoderOutputType, width, height, frameRate);
                    break;
                case FileFormats.mp4:
                    encoder = new H264Encoder(encoderOutputType, width, height, frameRate);
                    break;
            }

            encoder.Output = WriteSample;
        }

        public static IEnumerable<Guid> RecordingSubTypes()
        {
            return new Guid[]
            {
                MFMediaType.NV12,
                MFMediaType.YUY2,
                MFMediaType.IYUV,
                MFMediaType.YV12,
                MFMediaType.I420,
                MFMediaType.MJPG,
                MFMediaType.H264,
            };
        }
    }
}
