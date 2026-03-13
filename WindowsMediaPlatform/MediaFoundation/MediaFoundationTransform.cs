using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.Transform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationTransform : IDisposable
    {
        public static bool Succeeded(HResult hr) { return COMBase.Succeeded(hr); }
        public static bool Failed(HResult hr) { return COMBase.Failed(hr); }
        public static void SafeRelease(object o) { COMBase.SafeRelease(o); }

        protected IMFTransform transform;
        private SampleBuffer sampleBuffer;

        public delegate HResult ProcessDelegate(IMFSample sample);

        public ProcessDelegate Output;

        protected int width;
        protected int height;

        public int Width { get => width; }
        public int Height { get => height; }

        private object locker;

        private int inputCounter;
        private int outputCounter;
        private int counterDifference
        {
            get
            {
                return inputCounter - outputCounter;
            }
        }

        public int MaxBuffer { get; set; }

        public bool SingleSample { get; set; }

        protected virtual void Initialise(Guid transformCategory, IMFMediaType inputMediaType, IMFMediaType outputMediaType, MFT_EnumFlag unFlags = MFT_EnumFlag.None, bool setOutputFirst = true)
        {
            HResult hr = HResult.E_FAIL;

            MFTRegisterTypeInfo inputInfo = new MFTRegisterTypeInfo();
            inputMediaType.GetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, out inputInfo.guidMajorType);
            inputMediaType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out inputInfo.guidSubtype);

            MFTRegisterTypeInfo outputInfo = new MFTRegisterTypeInfo();
            outputMediaType.GetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, out outputInfo.guidMajorType);
            outputMediaType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out outputInfo.guidSubtype);

            transform = MFHelper.CreateTransform(transformCategory, unFlags, inputInfo, outputInfo);
            if (transform == null)
            {
                throw new Exception("Couldn't Create Transform: " + inputInfo.guidSubtype + " " + outputInfo.guidSubtype);
            }

            try
            {
                if (setOutputFirst)
                {
                    hr = transform.SetOutputType(0, outputMediaType, MFTSetTypeFlags.None);
                    MFError.ThrowExceptionForHR(hr);
                    hr = transform.SetInputType(0, inputMediaType, MFTSetTypeFlags.None);
                    MFError.ThrowExceptionForHR(hr);
                }
                else
                {
                    hr = transform.SetInputType(0, inputMediaType, MFTSetTypeFlags.None);
                    MFError.ThrowExceptionForHR(hr);
                    hr = transform.SetOutputType(0, outputMediaType, MFTSetTypeFlags.None);
                    MFError.ThrowExceptionForHR(hr);
                }
            }
            catch (Exception ex) 
            {
                Logger.VideoLog.LogException(this, ex);
                Logger.VideoLog.Log(this, MFDump.DumpAttribs(inputMediaType));
                Logger.VideoLog.Log(this, MFDump.DumpAttribs(outputMediaType));
                throw ex;
            }

            // Get the frame size.
            hr = MFExtern.MFGetAttributeSize(outputMediaType, MFAttributesClsid.MF_MT_FRAME_SIZE, out width, out height);
            MFError.ThrowExceptionForHR(hr);
        }


        protected MediaFoundationTransform()
        {
            locker = new object();
            MaxBuffer = 300;
            SingleSample = true;
        }

        protected void Initialise(IMFTransform transform)
        {
            HResult hr = HResult.E_FAIL;
            this.transform = transform;

            IMFMediaType outputMediaType;
            hr = transform.GetOutputCurrentType(0, out outputMediaType);
            MFError.ThrowExceptionForHR(hr);

            // Get the frame size.
            hr = MFExtern.MFGetAttributeSize(outputMediaType, MFAttributesClsid.MF_MT_FRAME_SIZE, out width, out height);
            MFError.ThrowExceptionForHR(hr);

            SafeRelease(outputMediaType);
        }
        
        public virtual void Dispose()
        {
            lock (locker)
            {
                SafeReleaseSampleBuffer(ref sampleBuffer);

                if (transform != null)
                {
                    SafeRelease(transform);
                    transform = null;
                }
            }
        }

        public virtual HResult ProcessInput(IMFSample sample)
        {
            if (sample == null)
                return HResult.S_FALSE;

            lock (locker)
            {
                HResult hr = HResult.E_FAIL;
                if (transform != null)
                {
                    hr = transform.ProcessInput(0, sample, 0);

                    // Too many start up errors to actually worry about.
                    //if (Failed(hr))
                    //{
                    //    Console.WriteLine(MFError.GetErrorText(hr));
                    //}
                    inputCounter++;
                }
                return hr;
            }
        }

        public virtual HResult ProcessOutput()
        {
            HResult hr = HResult.S_OK;

            while (Succeeded(hr))
            {
                lock (locker)
                {
                    if (transform == null)
                    {
                        return HResult.S_FALSE;
                    }

                    if (!SingleSample || sampleBuffer.sample == null)
                    {
                        sampleBuffer = CreateSampleBuffer();
                    }

                    ProcessOutputStatus outputStatus;
                    hr = transform.ProcessOutput(MFTProcessOutputFlags.None, 1, sampleBuffer.outputDataBuffer, out outputStatus);
                }

               // Console.WriteLine(this.GetType().Name +  " hr " + hr);

                if (Succeeded(hr) && Output != null)
                {
                    //Console.WriteLine(this.GetType().Name +  "output sample " + sampleBuffer.sample.GetSampleTime().TotalSeconds);
                    Output(sampleBuffer.sample);
                    outputCounter++;
                }

                if (!SingleSample)
                {
                    SafeReleaseSampleBuffer(ref sampleBuffer);
                }
            }

            // don't pass this one on, its fine.
            if (hr == HResult.MF_E_TRANSFORM_NEED_MORE_INPUT)
            {
                hr = HResult.S_OK;
            }
            return hr;
        }

        public string Debug()
        {
            return "input = " + inputCounter + ", output = " + outputCounter + ", difference = " + counterDifference;
        }

        public virtual IMFMediaType GetOutputCurrentType()
        {
            IMFMediaType output;
            HResult hr = transform.GetOutputCurrentType(0, out output);
            if (Failed(hr))
            {
                return null;
            }
            return output;
        }

        public static IEnumerable<IMFMediaType> GetInputMediaTypes(IMFTransform transform)
        {
            HResult hr = HResult.S_OK;

            int i = 0;
            while (Succeeded(hr))
            {
                IMFMediaType mediaType;
                hr = transform.GetInputAvailableType(0, i, out mediaType);
                i++;

                if (mediaType != null)
                {
                    yield return mediaType;
                }
            }
        }

        public static IEnumerable<IMFMediaType> GetOutputMediaTypes(IMFTransform transform)
        {
            HResult hr = HResult.S_OK;

            int i = 0;
            while (Succeeded(hr))
            {
                IMFMediaType mediaType;
                hr = transform.GetOutputAvailableType(0, i, out mediaType);
                i++;

                if (mediaType != null)
                {
                    yield return mediaType;
                }
            }
        }

        public virtual HResult Flush()
        {
            if (transform != null)
            {
                inputCounter = 0;
                outputCounter = 0;

                //transform.ProcessMessage(MFTMessageType.CommandDrain, IntPtr.Zero);
                transform.ProcessMessage(MFTMessageType.CommandFlush, IntPtr.Zero);
                return HResult.S_OK;
            }
            return HResult.E_FAIL;
        }

        protected virtual SampleBuffer CreateSampleBuffer()
        {
            SampleBuffer transformedSample = new SampleBuffer();

            MFHelper.CreateSample(width * height * 4, out transformedSample.sample, out transformedSample.mediaBuffer);

            transformedSample.outputDataBuffer = new MFTOutputDataBuffer[1];

            transformedSample.outputDataBuffer[0] = new MFTOutputDataBuffer();
            transformedSample.outputDataBuffer[0].dwStreamID = 0;

            IntPtr ptr = Marshal.GetComInterfaceForObject(transformedSample.sample, typeof(IMFSample));
            transformedSample.outputDataBuffer[0].pSample = ptr;

            return transformedSample;
        }

        protected void SafeReleaseSampleBuffer(ref SampleBuffer sampleBuffer)
        {
            SafeRelease(sampleBuffer.mediaBuffer);
            SafeRelease(sampleBuffer.sample);

            if (sampleBuffer.outputDataBuffer != null)
            {
                foreach (MFTOutputDataBuffer buffer in sampleBuffer.outputDataBuffer)
                {
                    if (buffer.pEvents != null)
                    {
                        SafeRelease(buffer.pEvents);
                        Marshal.Release(buffer.pSample);
                    }
                }
            }

            sampleBuffer.mediaBuffer = null;
            sampleBuffer.sample = null;
            sampleBuffer.outputDataBuffer = null;
        }

        protected struct SampleBuffer
        {
            public IMFMediaBuffer mediaBuffer;
            public IMFSample sample;
            public MFTOutputDataBuffer[] outputDataBuffer;
        }
    }
}
