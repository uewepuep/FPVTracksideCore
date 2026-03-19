using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using MediaFoundation.Transform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Owns the IMFSinkWriter lifecycle and all recording state.
    // Used by both MF and DS capture frame sources so encoding logic is not duplicated.
    //
    // Two entry points depending on how the capture side delivers frames:
    //   StartRecording(filename, inputType)      — MF path: NV12 → NVENC, SW encoder fallback
    //   StartRecording(filename, width, height)  — DS path: RGB32 → NVENC, no SW fallback
    //
    // Frame delivery:
    //   WriteSample(IMFSample)                   — MF path: handles timing + routes to writer or encoder
    //   WriteSampleFromBuffer(double, IntPtr, int) — DS path: wraps raw bytes as IMFSample then commits
    public class MFSinkWriterRecorder
    {
        private IMFSinkWriter writer;
        private int sink_stream;
        private MediaFoundationTransform encoder;
        private bool flushing;
        private bool hasBegun;
        private TimeSpan firstSampleTime;
        private long firstSampleTicks;
        private int receivedFrameCount;
        private TimeSpan receivedDuration;
        private TimeSpan recordingTargetLength;
        private readonly object writerLocker = new object();
        private readonly List<FrameTime> frameTimes = new List<FrameTime>();
        private readonly VideoConfig videoConfig;

        public bool Recording { get; private set; }
        public string Filename { get; private set; }
        public bool RecordNextFrameTime { get; set; }
        public bool ManualRecording { get; set; }
        public bool Finalising => flushing || encoder != null;
        public FileFormats FileFormat { get; set; }

        public enum FileFormats { WMV, mp4 }

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

        public MFSinkWriterRecorder(VideoConfig videoConfig)
        {
            this.videoConfig = videoConfig;
            FileFormat = FileFormats.mp4;
        }

        // --- MF path: NV12 → NVENC → H264. Falls back to software encoder on failure.
        // Pass isPassthrough=true when the source already outputs H264 (direct write, no encode).
        public bool StartRecording(string filename, IMFMediaType inputType, bool isPassthrough = false)
        {
            if (isPassthrough)
                return StartPassthrough(filename, inputType);

            if (inputType == null)
                return false;

            IMFMediaType h264Type = null;
            try
            {
                Logger.VideoLog.LogCall(this);
                ResetState(filename);

                lock (writerLocker)
                {
                    int targetHeight = videoConfig.RecordResolution;
                    int targetWidth = GetWidth(inputType, targetHeight);
                    int frameRate = videoConfig.RecordFrameRate;

                    h264Type = BuildH264OutputType(targetWidth, targetHeight, frameRate, inputType);

                    HResult hr = CreateSinkWriter(out writer);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.AddStream(h264Type, out sink_stream);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.SetInputMediaType(sink_stream, inputType, null);
                    MFError.ThrowExceptionForHR(hr);

                    MFHelper.LogSinkWriterTransforms(this, writer, sink_stream);

                    Recording = true;
                    RecordNextFrameTime = true;
                    lock (frameTimes) { frameTimes.Clear(); }
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                Logger.VideoLog.Log(this, "Hardware recording setup failed, falling back to software");
                StopRecording();
                return StartSoftware(filename, inputType);
            }
            finally
            {
                MFHelper.SafeRelease(h264Type);
            }
        }

        // --- DS path: RGB32 at given dimensions → NVENC → H264. No software fallback.
        public bool StartRecording(string filename, int width, int height)
        {
            IMFMediaType h264Type = null;
            IMFMediaType rgb32Type = null;
            try
            {
                Logger.VideoLog.LogCall(this);
                ResetState(filename);

                lock (writerLocker)
                {
                    int targetHeight = videoConfig.RecordResolution;
                    double aspect = (double)width / height;
                    double[] validAspects = new double[] { 4.0 / 3.0, 16.0 / 9.0 };
                    aspect = validAspects.OrderBy(a => Math.Abs(a - aspect)).First();
                    int targetWidth = ((int)(aspect * targetHeight) / 10) * 10;
                    int frameRate = videoConfig.RecordFrameRate;

                    h264Type = BuildH264OutputType(targetWidth, targetHeight, frameRate, null);

                    HResult hr;

                    hr = MFExtern.MFCreateMediaType(out rgb32Type);
                    MFError.ThrowExceptionForHR(hr);

                    hr = rgb32Type.SetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, MFMediaType.Video);
                    MFError.ThrowExceptionForHR(hr);

                    hr = rgb32Type.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, MFMediaType.RGB32);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeSize(rgb32Type, MFAttributesClsid.MF_MT_FRAME_SIZE, width, height);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeRatio(rgb32Type, MFAttributesClsid.MF_MT_FRAME_RATE, frameRate, 1);
                    MFError.ThrowExceptionForHR(hr);

                    hr = rgb32Type.SetUINT32(MFAttributesClsid.MF_MT_INTERLACE_MODE, 2);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeRatio(rgb32Type, MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                    MFError.ThrowExceptionForHR(hr);

                    // Negative stride = bottom-up, matching DS color space converter output
                    hr = rgb32Type.SetUINT32(MFAttributesClsid.MF_MT_DEFAULT_STRIDE, -(width * 4));
                    MFError.ThrowExceptionForHR(hr);

                    hr = CreateSinkWriter(out writer);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.AddStream(h264Type, out sink_stream);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.SetInputMediaType(sink_stream, rgb32Type, null);
                    MFError.ThrowExceptionForHR(hr);

                    MFHelper.LogSinkWriterTransforms(this, writer, sink_stream);

                    Recording = true;
                    RecordNextFrameTime = true;
                    lock (frameTimes) { frameTimes.Clear(); }
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                Logger.VideoLog.Log(this, "DS→MF recording setup failed");
                StopRecording();
                return false;
            }
            finally
            {
                MFHelper.SafeRelease(h264Type);
                MFHelper.SafeRelease(rgb32Type);
            }
        }

        private bool StartPassthrough(string filename, IMFMediaType h264InputType)
        {
            try
            {
                Logger.VideoLog.LogCall(this);
                ResetState(filename);

                lock (writerLocker)
                {
                    HResult hr = CreateSinkWriter(out writer);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.AddStream(h264InputType, out sink_stream);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.SetInputMediaType(sink_stream, h264InputType, null);
                    MFError.ThrowExceptionForHR(hr);

                    MFHelper.LogSinkWriterTransforms(this, writer, sink_stream);

                    Recording = true;
                    RecordNextFrameTime = true;
                    lock (frameTimes) { frameTimes.Clear(); }
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                StopRecording();
                return false;
            }
        }

        private bool StartSoftware(string filename, IMFMediaType inputType)
        {
            IMFMediaType format = null;
            try
            {
                Logger.VideoLog.LogCall(this);
                ResetState(filename);

                lock (writerLocker)
                {
                    CreateEncoder(inputType);

                    if (encoder == null)
                        return false;

                    format = encoder.GetOutputCurrentType();

                    if (format == null)
                        return false;

                    HResult hr = CreateSinkWriter(out writer);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.AddStream(format, out sink_stream);
                    MFError.ThrowExceptionForHR(hr);

                    hr = writer.SetInputMediaType(sink_stream, format, null);
                    MFError.ThrowExceptionForHR(hr);

                    MFHelper.LogSinkWriterTransforms(this, writer, sink_stream);

                    Recording = true;
                    RecordNextFrameTime = true;
                    lock (frameTimes) { frameTimes.Clear(); }
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                StopRecording();
                return false;
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

        // --- Frame delivery: MF path ---
        // Calculates normalised sample time, then routes to encoder (SW) or writer (NVENC/passthrough).
        // Encoder is checked first: in the SW path both encoder and writer are non-null, and raw frames
        // must go to the encoder rather than directly to the writer which expects compressed output.
        public void WriteSample(IMFSample sample)
        {
            if (!Recording && !flushing)
                return;

            TimeSpan normalizedTime = CalculateSampleTime(sample);
            sample.SetSampleTime(normalizedTime.Ticks);

            if (encoder != null)
            {
                encoder.ProcessInput(sample);
            }
            else if (writer != null)
            {
                CommitSample(sample, normalizedTime);
            }
        }

        // Drains the software encoder's output queue. Called from ProcessImage.
        public void ProcessEncoderOutput()
        {
            if (encoder == null)
                return;

            try
            {
                HResult hr = encoder.ProcessOutput();
                MFError.ThrowExceptionForHR(hr);
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                StopRecording();
            }
        }

        // --- Frame delivery: DS path ---
        // Converts raw DS callback bytes to an IMFSample and commits it to the sink writer.
        public void WriteSampleFromBuffer(double sampleTime, IntPtr buffer, int bufferLen)
        {
            if ((!Recording && !flushing) || buffer == IntPtr.Zero || bufferLen == 0)
                return;

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

                hr = sample.SetSampleTime(normalizedTicks);
                MFError.ThrowExceptionForHR(hr);

                long duration = (long)(10_000_000.0 / videoConfig.VideoMode.FrameRate);
                hr = sample.SetSampleDuration(duration);
                MFError.ThrowExceptionForHR(hr);

                CommitSample(sample, receivedDuration);
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

        public void CleanUp()
        {
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

            encoder?.Dispose();
            encoder = null;
            flushing = false;
        }

        // --- Internals ---

        private TimeSpan CalculateSampleTime(IMFSample sample)
        {
            receivedFrameCount++;

            TimeSpan sampleTime = sample.GetSampleTime();

            if (receivedFrameCount == 1)
                firstSampleTime = sampleTime;

            receivedDuration = sampleTime - firstSampleTime;
            TimeSpan normalizedTime = sampleTime - firstSampleTime;

            if (RecordNextFrameTime)
            {
                lock (frameTimes)
                {
                    frameTimes.Add(new FrameTime() { Frame = receivedFrameCount, Time = DateTime.Now, Seconds = normalizedTime.TotalSeconds });
                }

                RecordNextFrameTime = false;
            }

            return normalizedTime;
        }

        private void CommitSample(IMFSample sample, TimeSpan sampleTime)
        {
            lock (writerLocker)
            {
                if (writer == null || sample == null)
                    return;

                HResult hr;

                if (!hasBegun)
                {
                    hr = writer.BeginWriting();
                    MFError.ThrowExceptionForHR(hr);
                    hasBegun = true;
                }

                hr = writer.WriteSample(sink_stream, sample);
                MFError.ThrowExceptionForHR(hr);

                if (flushing && sampleTime >= recordingTargetLength)
                {
                    Logger.VideoLog.LogCall(this, "Final Length " + sampleTime);
                    flushing = false;
                    FlushFinalize();
                }
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

        private void ResetState(string filename)
        {
            Filename = filename + "." + FileFormat.ToString().ToLower();
            hasBegun = false;

            if (Recording)
                StopRecording();

            flushing = false;
            receivedDuration = TimeSpan.Zero;
            recordingTargetLength = TimeSpan.Zero;
            firstSampleTime = TimeSpan.Zero;
            firstSampleTicks = 0;
            receivedFrameCount = 0;

            if (File.Exists(Filename))
                File.Delete(Filename);
        }

        private HResult CreateSinkWriter(out IMFSinkWriter sinkWriter)
        {
            IMFAttributes attributes = null;
            try
            {
                HResult hr = MFExtern.MFCreateAttributes(out attributes, 2);
                if (MFHelper.Failed(hr)) { sinkWriter = null; return hr; }

                hr = attributes.SetUINT32(MFAttributesClsid.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                if (MFHelper.Failed(hr)) { sinkWriter = null; return hr; }

                hr = attributes.SetUINT32(MFAttributesClsid.MF_LOW_LATENCY, 1);
                if (MFHelper.Failed(hr)) { sinkWriter = null; return hr; }

                return MFExtern.MFCreateSinkWriterFromURL(Filename, null, attributes, out sinkWriter);
            }
            finally
            {
                MFHelper.SafeRelease(attributes);
            }
        }

        private IMFMediaType BuildH264OutputType(int targetWidth, int targetHeight, int frameRate, IMFMediaType pixelAspectSource)
        {
            IMFMediaType h264Type = null;
            try
            {
                HResult hr;

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

                if (pixelAspectSource != null)
                    MFHelper.CopyAttribute(pixelAspectSource, h264Type, MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO);
                else
                    MFExtern.MFSetAttributeRatio(h264Type, MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);

                int bitRate = H264Encoder.GetBitRate(targetWidth, targetHeight, frameRate);
                hr = h264Type.SetUINT32(MFAttributesClsid.MF_MT_AVG_BITRATE, bitRate);
                MFError.ThrowExceptionForHR(hr);

                hr = h264Type.SetUINT32(MFAttributesClsid.MF_MT_MPEG2_PROFILE, 77); // Main
                MFError.ThrowExceptionForHR(hr);

                return h264Type;
            }
            catch
            {
                MFHelper.SafeRelease(h264Type);
                throw;
            }
        }

        private void CreateEncoder(IMFMediaType inputType)
        {
            int height = videoConfig.RecordResolution;
            int width = GetWidth(inputType, height);
            int frameRate = videoConfig.RecordFrameRate;

            switch (FileFormat)
            {
                case FileFormats.WMV:
                    encoder = new WMVEncoder(inputType, width, height, frameRate);
                    break;
                case FileFormats.mp4:
                    encoder = new H264Encoder(inputType, width, height, frameRate);
                    break;
            }

            encoder.Output = CommitEncoderSample;
        }

        private HResult CommitEncoderSample(IMFSample sample)
        {
            CommitSample(sample, sample.GetSampleTime());
            return HResult.S_OK;
        }

        public static int GetWidth(IMFMediaType outputMediaType, int targetHeight)
        {
            int frameWidth, frameHeight;

            HResult hr = MFExtern.MFGetAttributeSize(outputMediaType, MFAttributesClsid.MF_MT_FRAME_SIZE, out frameWidth, out frameHeight);
            MFError.ThrowExceptionForHR(hr);

            double[] validAspects = new double[] { 4.0 / 3.0, 16.0 / 9.0 };

            double thisAspect = frameWidth / (double)frameHeight;
            thisAspect = validAspects.OrderBy(n => Math.Abs(n - thisAspect)).First();

            int width = (int)(thisAspect * targetHeight);
            width /= 10;
            width *= 10;

            return width;
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
