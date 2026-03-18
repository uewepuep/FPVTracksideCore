using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using MediaFoundation.Transform;
using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Tools;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationCaptureFrameSource : MediaFoundationDeviceFrameSource, ICaptureFrameSource
    {
        protected IMFSinkWriter writer;
        protected int sink_stream;

        protected MediaFoundationTransform encoder;

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

        public virtual void StartRecording(string filename)
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
            if ((Recording || flushing) && MFHelper.Succeeded(hr) && encoder != null)
            {
                TimeSpan newSampleTime = CalculateSampleTime(sample);
                sample.SetSampleTime(newSampleTime.Ticks);

                encoder.ProcessInput(sample);
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
                //Logger.VideoLog.Log(this, "first Sample Time: " + firstSampleTime);
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

            //Logger.VideoLog.Log(this, "Sample Time: " + sampleTime + " New Sample Time: " + newSampleTime + " dur: " + dur);

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

            // Get the frame size.
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
                // Get the actual current Type
                Guid subType;
                hr = sourceMediaType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out subType);
                MFError.ThrowExceptionForHR(hr);
                
                // get the size
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
