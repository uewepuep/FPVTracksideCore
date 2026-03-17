using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using DirectShowLib;
using ImageServer;
using MediaFoundation;
using MediaFoundation.Alt;
using MediaFoundation.EVR;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using MediaFoundation.Transform;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    public abstract class MediaFoundationFrameSource : TextureFrameSource, IMFSourceReaderCallback
    {
        

        protected IMFSourceReader reader;
        protected IMFSourceReaderAsync readerAsync;
        protected ColorProcessor colorProcessor;
        protected DecoderProcessor decoderProcessor;

        private int width;
        private int height;

        public override int FrameHeight => height;
        public override int FrameWidth => width;
        public override SurfaceFormat FrameFormat => SurfaceFormat.Bgr32;

        public bool AutomaticVideoConversion { get; set; }
        public bool CanUseDecoderProcessor { get; set; }

        public MediaFoundationFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {
            width = 0;
            height = 0;
            reader = null;
            AutomaticVideoConversion = false;

            HResult hr = MFExtern.MFStartup(0x20070, MFStartup.Full);
            MFError.ThrowExceptionForHR(hr);
        }

        protected IMFMediaType[] GetNativeMediaTypes()
        {

            List<IMFMediaType> mediaTypes = new List<IMFMediaType>();
            if (reader == null)
                return mediaTypes.ToArray();

            for (int i = 0; ; i++)
            {
                IMFMediaType pType = GetNativeMediaType(i);
                if (pType == null)
                    break;
                mediaTypes.Add(pType);
            }

            return mediaTypes.ToArray();
        }

        protected IMFMediaType GetNativeMediaType(int index)
        {
            HResult hr;
            IMFMediaType pType;
            hr = reader.GetNativeMediaType((int)MF_SOURCE_READER.FirstVideoStream, index, out pType);
            if (MFHelper.Failed(hr))
            {
                return null;
            }
            return pType;
        }

        protected IMFMediaType GetCurrentMediaType()
        {
            IMFMediaType currentType = null;
            HResult hr = reader.GetCurrentMediaType((int)MF_SOURCE_READER.FirstVideoStream, out currentType);
        
            if (MFHelper.Succeeded(hr))
            {
                return currentType;
            }
            return null;
        }

        public override void CleanUp()
        {
            base.CleanUp();
            if (reader != null)
            {
                MFHelper.SafeRelease(reader);
                reader = null;
            }

            if (colorProcessor != null)
            {
                colorProcessor.Dispose();
            }
        }

        public override bool Start()
        {
            if (reader != null)
            {
                // Ask for the first sample.
                HResult hr = ReadASync();
                if (MFHelper.Succeeded(hr))
                {
                    Connected = true;
                }
            }
            return base.Start();
        }

        public override bool Stop()
        {
            return base.Stop();
        }

        public virtual HResult OnReadSample(HResult hrStatus, int dwStreamIndex, MF_SOURCE_READER_FLAG dwStreamFlags, long timestep, IMFSample sample)
        {
            HResult hr = hrStatus;

            if (sample != null)
            {
                hr = ProcessRaw(sample);
            }

            if (MFHelper.Succeeded(hrStatus) && State == States.Running)
            {
                // Ask for the next sample.
                hr = readerAsync.ReadSample((int)MF_SOURCE_READER.FirstVideoStream, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                MFError.ThrowExceptionForHR(hr);
            }
            MFHelper.SafeRelease(sample);
            return HResult.S_OK;
        }

        public HResult Read(out IMFSample sample)
        {
            int streamIndex;
            MF_SOURCE_READER_FLAG streamFlags; 
            long timestamp;

            return reader.ReadSample((int)MF_SOURCE_READER.FirstVideoStream, 0, out streamIndex, out streamFlags, out timestamp, out sample);
        }

        public HResult ReadASync()
        {
            if (!ASync)
                return HResult.S_FALSE;

            return readerAsync.ReadSample((int)MF_SOURCE_READER.FirstVideoStream, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        public override bool Unpause()
        {
            bool result = base.Unpause();
            // Ask for the next sample.
            HResult hr = ReadASync();

            return MFHelper.Succeeded(hr);
        }

        protected virtual HResult ProcessRaw(IMFSample sample)
        {
            HResult hr = HResult.S_OK;
            
            if (decoderProcessor != null) 
            { 
                decoderProcessor.ProcessInput(sample);
                decoderProcessor.ProcessOutput();
            }
            else
            {
                return ProcessUncompressed(sample);
            }

            return hr;
        }

        protected virtual HResult ProcessUncompressed(IMFSample sample)
        {
            HResult hr = HResult.S_OK;

            if (colorProcessor == null)
            {
                ProcessRGBSample(sample);
            }
            else
            {
                hr = colorProcessor.ProcessInput(sample);
            }

            NotifyReceivedFrame();
            return hr;
        }

        protected override void ProcessImage()
        {
            if (colorProcessor != null)
            {
                colorProcessor.ProcessOutput();
            }
            base.ProcessImage();
        }

        protected virtual HResult ProcessRGBSample(IMFSample sample)
        {
            var currentRawTextures = rawTextures;
            if (currentRawTextures != null)
            {
                IMFMediaBuffer buffer = null;
                try
                {
                    RawTexture frame;

                    if (currentRawTextures.GetWritable(out frame))
                    {
                        IntPtr intPtr;
                        int length;
                        int current;

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
                            hr = buffer.Lock(out intPtr, out length, out current);
                            MFError.ThrowExceptionForHR(hr);

                            frame.SetData(intPtr, sampleTime, FrameProcessNumber);

                            hr = buffer.Unlock();
                            MFError.ThrowExceptionForHR(hr);

                            currentRawTextures.WriteOne(frame);
                        }
                    }
                }
                finally
                {
                    if (buffer != null)
                    {
                        MFHelper.SafeRelease(buffer);
                    }
                }
            }
            return HResult.S_OK;
        }

        public HResult OnFlush(int dwStreamIndex)
        {
            return HResult.S_OK;
        }

        public HResult OnEvent(int dwStreamIndex, IMFMediaEvent pEvent)
        {
            return HResult.S_OK;
        }

        protected virtual HResult CreateReader(IMFMediaSource pSource)
        {
            HResult hr = HResult.S_OK;
            IMFAttributes pAttributes = null;
            IMFMediaType nativeType = null;
            try
            {
                hr = MFExtern.MFCreateAttributes(out pAttributes, 2);
                MFError.ThrowExceptionForHR(hr);

                //hr = pAttributes.SetUINT32(MFAttributesClsid.MF_READWRITE_DISABLE_CONVERTERS, 1);
                //MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 0);
                MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                MFError.ThrowExceptionForHR(hr);

                hr = pAttributes.SetUINT32(MFAttributesClsid.MF_SOURCE_READER_DISABLE_DXVA, 0);
                MFError.ThrowExceptionForHR(hr);

                if (AutomaticVideoConversion)
                {
                    hr = pAttributes.SetUINT32(MFAttributesClsid.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1);
                    MFError.ThrowExceptionForHR(hr);
                }

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
                MFHelper.SafeRelease(nativeType);
            }

            return HResult.E_FAIL;
        }

        protected virtual HResult SetupReader()
        {
            HResult hr = HResult.S_OK;
            IMFAttributes pAttributes = null;
            IMFMediaType currentType = null;
            IMFMediaType outputType = null;

            try
            {
                readerAsync = (IMFSourceReaderAsync)reader;

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
                {
                    Direction = stride < 0 ? Directions.TopDown : Directions.BottomUp;
                }
                else
                {
                    Direction = MFHelper.GetDirection(outputSubType);
                }

                string format = MFHelper.GetFormat(currentSubType);
                Tools.Logger.VideoLog.Log(this, VideoConfig.DeviceName, "Width: " + width + " Height: " + height + " Format: " + format);
                rawTextures = new XBuffer<RawTexture>(5, width, height);
                return HResult.S_OK;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                Logger.VideoLog.Log(this, "Type: ", MFDump.DumpAttribs(outputType));
            }
            finally
            {
                MFHelper.SafeRelease(pAttributes);
                MFHelper.SafeRelease(currentType);
            }
            
            return HResult.E_FAIL;
        }

        protected virtual HResult SetupTransforms(out IMFMediaType sourceMediaType, out IMFMediaType outputMediaType)
        {
            if (reader == null)
            {
                sourceMediaType = null;
                outputMediaType = null;
                return HResult.E_FAIL;
            }

            HResult hr = reader.GetCurrentMediaType((int)MF_SOURCE_READER.FirstVideoStream, out sourceMediaType);

            if (hr != HResult.S_OK)
            {
                sourceMediaType = null;
                outputMediaType = null;
                return hr;
            }

            // Get the actual current Type
            Guid subType;
            hr = sourceMediaType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out subType);
            MFError.ThrowExceptionForHR(hr);

            if (DecoderProcessor.SupportedInputTypes().Contains(subType))
            {
                decoderProcessor = new DecoderProcessor(sourceMediaType, MFMediaType.NV12);
                outputMediaType = decoderProcessor.DestinationType;
                decoderProcessor.Output = ProcessUncompressed;
            }
            else
            {
                hr = MFExtern.MFCreateMediaType(out outputMediaType);
                MFError.ThrowExceptionForHR(hr);

                hr = sourceMediaType.CopyAllItems(outputMediaType);
                MFError.ThrowExceptionForHR(hr);
            }

            if (AutomaticVideoConversion)
            {
                hr = MFExtern.MFCreateMediaType(out outputMediaType);
                MFError.ThrowExceptionForHR(hr);

                hr = sourceMediaType.CopyAllItems(outputMediaType);
                MFError.ThrowExceptionForHR(hr);

                // Set the type to RGB.
                hr = outputMediaType.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, MFMediaType.RGB32);
                MFError.ThrowExceptionForHR(hr);

                hr = MFExtern.MFGetAttributeSize(outputMediaType, MFAttributesClsid.MF_MT_FRAME_SIZE, out width, out height);
                MFError.ThrowExceptionForHR(hr);

                hr = reader.SetCurrentMediaType((int)MF_SOURCE_READER.FirstVideoStream, null, outputMediaType);
                MFError.ThrowExceptionForHR(hr);
            }
            else
            {
                colorProcessor = new ColorProcessor(outputMediaType, MFMediaType.RGB32);
                colorProcessor.Output = ProcessRGBSample;

                height = colorProcessor.Height;
                width = colorProcessor.Width;
            }

            return hr;
        }
    }
}
