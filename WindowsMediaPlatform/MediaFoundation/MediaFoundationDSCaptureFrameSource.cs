using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tools;
using WindowsMediaPlatform.DirectShow;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Captures frames via DirectShow (for DS-only devices such as OBS Virtual Camera),
    // but records through an IMFSinkWriter with MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS
    // so the pipeline uses NVENC/QSV/AMF rather than GMFBridge/WMV.
    //
    // Display path:  DS filter graph → color space converter → RGB32 → rawTextures (base class)
    // Recording path: BufferCB RGB32 → IMFSample → IMFSinkWriter → NVENC → H264/mp4
    public class MediaFoundationDSCaptureFrameSource : DirectShowDeviceFrameSource, ICaptureFrameSource
    {
        private IMFSinkWriter writer;
        private int sink_stream;
        private bool flushing;
        private bool hasBegun;
        private long firstSampleTicks;
        private int receivedFrameCount;
        private TimeSpan receivedDuration;
        private TimeSpan recordingTargetLength;
        private readonly object writerLocker = new object();
        private readonly List<FrameTime> frameTimes = new List<FrameTime>();

        public string Filename { get; private set; }
        public bool RecordNextFrameTime { get; set; }
        public bool ManualRecording { get; set; }
        public bool Finalising => flushing;
        public MediaFoundationCaptureFrameSource.FileFormats FileFormat { get; set; }

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

        public MediaFoundationDSCaptureFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {
            FileFormat = MediaFoundationCaptureFrameSource.FileFormats.mp4;

            HResult hr = MFExtern.MFStartup(0x20070, MFStartup.Full);
            MFError.ThrowExceptionForHR(hr);
        }

        public override IEnumerable<Mode> GetModes()
        {
            return base.GetModes().Select(m => new Mode
            {
                Width = m.Width,
                Height = m.Height,
                FrameRate = m.FrameRate,
                Format = m.Format,
                Index = m.Index,
                FrameWork = FrameWork.MediaFoundation
            });
        }

        // --- Recording setup ---

        public void StartRecording(string filename)
        {
            IMFMediaType h264Type = null;
            IMFMediaType rgb32Type = null;
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
                firstSampleTicks = 0;
                receivedFrameCount = 0;

                if (File.Exists(Filename))
                    File.Delete(Filename);

                HResult hr;

                lock (writerLocker)
                {
                    int targetHeight = VideoConfig.RecordResolution;
                    double aspect = (double)FrameWidth / FrameHeight;
                    double[] validAspects = new double[] { 4.0 / 3.0, 16.0 / 9.0 };
                    aspect = validAspects.OrderBy(a => Math.Abs(a - aspect)).First();
                    int targetWidth = ((int)(aspect * targetHeight) / 10) * 10;
                    int frameRate = VideoConfig.RecordFrameRate;

                    // H264 output type
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

                    hr = MFExtern.MFSetAttributeRatio(h264Type, MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                    MFError.ThrowExceptionForHR(hr);

                    int bitRate = H264Encoder.GetBitRate(targetWidth, targetHeight, frameRate);
                    hr = h264Type.SetUINT32(MFAttributesClsid.MF_MT_AVG_BITRATE, bitRate);
                    MFError.ThrowExceptionForHR(hr);

                    hr = h264Type.SetUINT32(MFAttributesClsid.MF_MT_MPEG2_PROFILE, 77); // Main
                    MFError.ThrowExceptionForHR(hr);

                    // RGB32 input type matching DS output.
                    // DS color space converter produces bottom-up RGB32; negative stride signals
                    // this to MF so the color converter inserts correctly before NVENC.
                    hr = MFExtern.MFCreateMediaType(out rgb32Type);
                    MFError.ThrowExceptionForHR(hr);

                    hr = rgb32Type.SetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, MFMediaType.Video);
                    MFError.ThrowExceptionForHR(hr);

                    hr = rgb32Type.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, MFMediaType.RGB32);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeSize(rgb32Type, MFAttributesClsid.MF_MT_FRAME_SIZE, FrameWidth, FrameHeight);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeRatio(rgb32Type, MFAttributesClsid.MF_MT_FRAME_RATE, frameRate, 1);
                    MFError.ThrowExceptionForHR(hr);

                    hr = rgb32Type.SetUINT32(MFAttributesClsid.MF_MT_INTERLACE_MODE, 2); // Progressive
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeRatio(rgb32Type, MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                    MFError.ThrowExceptionForHR(hr);

                    // Negative stride = bottom-up (two's complement as uint)
                    hr = rgb32Type.SetUINT32(MFAttributesClsid.MF_MT_DEFAULT_STRIDE, -(FrameWidth * 4));
                    MFError.ThrowExceptionForHR(hr);

                    // Create sink writer with hardware transforms so it picks up NVENC/QSV/AMF
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

                    hr = writer.SetInputMediaType(sink_stream, rgb32Type, null);
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
                Logger.VideoLog.Log(this, "DS→MF recording setup failed, no recording");
                StopRecording();
            }
            finally
            {
                MFHelper.SafeRelease(h264Type);
                MFHelper.SafeRelease(rgb32Type);
                MFHelper.SafeRelease(attributes);
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

        // --- Frame delivery: DS callback → IMFSample → sink writer ---

        public override int BufferCB(double sampleTime, IntPtr buffer, int bufferLen)
        {
            int result = base.BufferCB(sampleTime, buffer, bufferLen);

            if ((Recording || flushing) && buffer != IntPtr.Zero && bufferLen > 0)
            {
                long sampleTicks = (long)(sampleTime * 10_000_000);

                receivedFrameCount++;
                if (receivedFrameCount == 1)
                    firstSampleTicks = sampleTicks;

                long normalizedTicks = sampleTicks - firstSampleTicks;
                receivedDuration = TimeSpan.FromTicks(normalizedTicks);

                if (RecordNextFrameTime)
                {
                    lock (frameTimes)
                    {
                        frameTimes.Add(new FrameTime() { Frame = receivedFrameCount, Time = DateTime.Now, Seconds = receivedDuration.TotalSeconds });
                    }

                    RecordNextFrameTime = false;
                }

                WriteSampleFromBuffer(normalizedTicks, buffer, bufferLen);
            }

            return result;
        }

        private void WriteSampleFromBuffer(long sampleTicks, IntPtr buffer, int bufferLen)
        {
            IMFSample sample = null;
            IMFMediaBuffer mfBuffer = null;

            try
            {
                HResult hr = MFExtern.MFCreateMemoryBuffer(bufferLen, out mfBuffer);
                MFError.ThrowExceptionForHR(hr);

                IntPtr dst;
                int maxLen;
                int curLen;

                hr = mfBuffer.Lock(out dst, out maxLen, out curLen);
                MFError.ThrowExceptionForHR(hr);

                MFHelper.CopyMemory(dst, buffer, (uint)bufferLen);

                hr = mfBuffer.Unlock();
                MFError.ThrowExceptionForHR(hr);

                hr = mfBuffer.SetCurrentLength(bufferLen);
                MFError.ThrowExceptionForHR(hr);

                hr = MFExtern.MFCreateSample(out sample);
                MFError.ThrowExceptionForHR(hr);

                hr = sample.AddBuffer(mfBuffer);
                MFError.ThrowExceptionForHR(hr);

                hr = sample.SetSampleTime(sampleTicks);
                MFError.ThrowExceptionForHR(hr);

                long duration = (long)(10_000_000.0 / VideoConfig.VideoMode.FrameRate);
                hr = sample.SetSampleDuration(duration);
                MFError.ThrowExceptionForHR(hr);

                WriteSample(sample);
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                StopRecording();
            }
            finally
            {
                MFHelper.SafeRelease(mfBuffer);
                MFHelper.SafeRelease(sample);
            }
        }

        private void WriteSample(IMFSample sample)
        {
            lock (writerLocker)
            {
                if (writer != null && sample != null)
                {
                    HResult hr;

                    if (!hasBegun)
                    {
                        hr = writer.BeginWriting();
                        MFError.ThrowExceptionForHR(hr);
                        hasBegun = true;
                    }

                    hr = writer.WriteSample(sink_stream, sample);
                    MFError.ThrowExceptionForHR(hr);

                    if (flushing && receivedDuration >= recordingTargetLength)
                    {
                        Logger.VideoLog.LogCall(this, "Final Length " + receivedDuration);
                        flushing = false;
                        FlushFinalize();
                    }
                }
            }
        }

        private void FlushFinalize()
        {
            Logger.VideoLog.LogCall(this);

            try
            {
                HResult hr;

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

        // --- Cleanup ---

        public override void CleanUp()
        {
            if (Recording)
                StopRecording();

            lock (writerLocker)
            {
                if (writer != null)
                {
                    try
                    {
                        writer.Flush(0);
                        writer.Finalize_();
                    }
                    catch (Exception e)
                    {
                        Logger.VideoLog.LogException(this, e);
                    }

                    MFHelper.SafeRelease(writer);
                    writer = null;
                }
            }

            flushing = false;
            base.CleanUp();
        }
    }
}
