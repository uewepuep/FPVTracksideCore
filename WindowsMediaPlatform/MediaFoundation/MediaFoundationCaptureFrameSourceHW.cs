using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using System;
using System.IO;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Extends MediaFoundationCaptureFrameSource with hardware H264 encoding.
    // Feeds raw NV12 directly to the IMFSinkWriter with MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,
    // so the sink writer drives NVENC/Quick Sync/AMF internally and handles async MFT complexity.
    // Falls back to the base (software) StartRecording if hardware setup fails.
    public class MediaFoundationCaptureFrameSourceHW : MediaFoundationCaptureFrameSource
    {
        public MediaFoundationCaptureFrameSourceHW(VideoConfig videoConfig)
            : base(videoConfig)
        {
        }

        // --- Recording setup: NV12 → IMFSinkWriter (hardware encoder inside) ---

        public override void StartRecording(string filename)
        {
            // Passthrough (camera already outputs H264) and null encoder type are handled
            // by the base class unchanged.
            if (encoderOutputType == null || passthroughCompressed)
            {
                base.StartRecording(filename);
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
                base.StartRecording(filename);
            }
            finally
            {
                MFHelper.SafeRelease(h264Type);
                MFHelper.SafeRelease(attributes);
            }
        }

        // --- Frame delivery: write NV12 directly to sink writer ---

        protected override HResult ProcessUncompressed(IMFSample sample)
        {
            // Call base for display side (colorProcessor, NotifyReceivedFrame).
            // encoder is null so the base recording path is skipped.
            HResult hr = base.ProcessUncompressed(sample);

            if ((Recording || flushing) && MFHelper.Succeeded(hr))
            {
                TimeSpan newSampleTime = CalculateSampleTime(sample);
                sample.SetSampleTime(newSampleTime.Ticks);
                WriteSample(sample);
            }

            return hr;
        }
    }
}
