using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.Transform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class H264Encoder : MediaFoundationTransform
    {
        private AutoTransform resizer;

        public H264Encoder(IMFMediaType inputType, int width, int height, int frameRate) 
            : base()
        {
            IMFMediaType encoderOutput = null;
            IMFMediaType encoderInput = null;

            try
            {
                HResult hr;

                Guid inputSubType;
                inputType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out inputSubType);

                MFTRegisterTypeInfo inType = new MFTRegisterTypeInfo();
                inType.guidMajorType = MFMediaType.Video;
                inType.guidSubtype = inputSubType;

                if (inType.guidSubtype == MFMediaType.I420)
                {
                    inType.guidSubtype = MFMediaType.IYUV;
                }

                MFTRegisterTypeInfo outType = new MFTRegisterTypeInfo();
                outType.guidMajorType = MFMediaType.Video;
                outType.guidSubtype = MFMediaType.H264;

                IMFTransform encoder = MFHelper.CreateTransform(MFTransformCategory.MFT_CATEGORY_VIDEO_ENCODER, 0, inType, outType);

                if (encoder == null)
                {
                    throw new Exception("Couldn't create encoder to do " + MFHelper.GetFormat(inType.guidSubtype) + " to " + MFHelper.GetFormat(outType.guidSubtype));
                }

                hr = encoder.GetOutputAvailableType(0, 0, out encoderOutput);

                hr = encoder.GetInputAvailableType(0, 0, out encoderInput);

                if (encoderInput == null)
                {
                    hr = MFExtern.MFCreateMediaType(out encoderInput);
                    MFError.ThrowExceptionForHR(hr);
                }

                hr = MFHelper.CopyAttribute(inputType, encoderInput, MFAttributesClsid.MF_MT_MAJOR_TYPE);
                MFError.ThrowExceptionForHR(hr);

                hr = encoderInput.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, inType.guidSubtype);
                MFError.ThrowExceptionForHR(hr);

                Guid[] toCopy = new Guid[]
                {
                    MFAttributesClsid.MF_MT_FRAME_SIZE,
                    MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO,
                    MFAttributesClsid.MF_MT_INTERLACE_MODE,
                    MFAttributesClsid.MF_MT_FRAME_RATE
                };

                foreach (Guid attributeId in toCopy)
                {
                    hr = MFHelper.CopyAttribute(inputType, encoderOutput, attributeId);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFHelper.CopyAttribute(inputType, encoderInput, attributeId);
                    MFError.ThrowExceptionForHR(hr);
                }

                int inputWidth, inputHeight;
                // Get the frame size.
                hr = MFExtern.MFGetAttributeSize(inputType, MFAttributesClsid.MF_MT_FRAME_SIZE, out inputWidth, out inputHeight);
                MFError.ThrowExceptionForHR(hr);

                int a, b;
                hr = MFExtern.MFGetAttributeRatio(encoderOutput, MFAttributesClsid.MF_MT_FRAME_RATE, out a, out b);
                MFError.ThrowExceptionForHR(hr);

                int actualFrameRate = (int)Math.Ceiling(a / (float)b);

                if (inputWidth != width || inputHeight != height || actualFrameRate != frameRate)
                {
                    resizer = new AutoTransform(encoderInput, width, height, frameRate);
                    resizer.Output = base.ProcessInput;

                    hr = MFExtern.MFSetAttributeSize(encoderInput, MFAttributesClsid.MF_MT_FRAME_SIZE, width, height);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeSize(encoderOutput, MFAttributesClsid.MF_MT_FRAME_SIZE, width, height);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeRatio(encoderInput, MFAttributesClsid.MF_MT_FRAME_RATE, frameRate, 1);
                    MFError.ThrowExceptionForHR(hr);

                    hr = MFExtern.MFSetAttributeRatio(encoderOutput, MFAttributesClsid.MF_MT_FRAME_RATE, frameRate, 1);
                    MFError.ThrowExceptionForHR(hr);
                }

                int bitRate = GetBitRate(width, height, frameRate);
                hr = encoderOutput.SetUINT32(MFAttributesClsid.MF_MT_AVG_BITRATE, bitRate);
                MFError.ThrowExceptionForHR(hr);

                hr = encoderOutput.SetUINT32(MFAttributesClsid.MF_MT_MPEG2_PROFILE, (int)eAVEncH264VProfile.eAVEncH264VProfile_Main);
                MFError.ThrowExceptionForHR(hr);

                string format = MFHelper.GetFormat(outType.guidSubtype);
                Tools.Logger.VideoLog.Log(this, "H264Encoder", MFHelper.GetFormat(inType.guidSubtype) + " to Width: " + width + " Height: " + height + " Format: " + format);

                try
                {
                    hr = encoder.SetOutputType(0, encoderOutput, MFTSetTypeFlags.None);
                    MFError.ThrowExceptionForHR(hr);

                    hr = encoder.SetInputType(0, encoderInput, MFTSetTypeFlags.None);
                    MFError.ThrowExceptionForHR(hr);
                }
                catch (Exception e)
                {
                    string outDump = MFDump.DumpAttribs(encoderOutput);
                    string inDump = MFDump.DumpAttribs(encoderInput);

                    Tools.Logger.VideoLog.LogException(this, e);
                    Tools.Logger.VideoLog.Log(this, "Output", outDump);
                    Tools.Logger.VideoLog.Log(this, "Input", inDump);
                    throw;
                }

                Initialise(encoder);
            }
            finally
            {
                SafeRelease(encoderOutput);
                SafeRelease(encoderInput);
            }
        }

        public override void Dispose()
        {
            if (resizer != null)
            {
                resizer.Dispose();
                resizer = null;
            }
            base.Dispose();
        }

        public override HResult ProcessInput(IMFSample sample)
        {
            if (resizer != null)
            {
                resizer.ProcessInput(sample);
                return resizer.ProcessOutput();
            }
            else
            {
                return base.ProcessInput(sample);
            }
        }

        private static int GetBitRate(int width, int height, int frameRate)
        {
            int goodBitrate = 12000000;
            int goodPixelCount = 1280 * 720 * 30;
            float ratio = goodBitrate / (float)goodPixelCount;

            int thisPixelCount = width * height * frameRate;

            return (int)(ratio * thisPixelCount);
        }

        public override HResult Flush()
        {
            if (resizer != null)
            {
                resizer.Flush();
            }
            return base.Flush();
        }


        enum eAVEncH264VProfile
        {
            eAVEncH264VProfile_unknown = 0,
            eAVEncH264VProfile_Simple = 66,
            eAVEncH264VProfile_Base = 66,
            eAVEncH264VProfile_Main = 77,
            eAVEncH264VProfile_High = 100,
            eAVEncH264VProfile_422 = 122,
            eAVEncH264VProfile_High10 = 110,
            eAVEncH264VProfile_444 = 144,
            eAVEncH264VProfile_Extended = 88
        };

        enum eAVEncH264VLevel
        {
            eAVEncH264VLevel1 = 10,
            eAVEncH264VLevel1_b = 11,
            eAVEncH264VLevel1_1 = 11,
            eAVEncH264VLevel1_2 = 12,
            eAVEncH264VLevel1_3 = 13,
            eAVEncH264VLevel2 = 20,
            eAVEncH264VLevel2_1 = 21,
            eAVEncH264VLevel2_2 = 22,
            eAVEncH264VLevel3 = 30,
            eAVEncH264VLevel3_1 = 31,
            eAVEncH264VLevel3_2 = 32,
            eAVEncH264VLevel4 = 40,
            eAVEncH264VLevel4_1 = 41,
            eAVEncH264VLevel4_2 = 42,
            eAVEncH264VLevel5 = 50,
            eAVEncH264VLevel5_1 = 51,
            eAVEncH264VLevel5_2 = 52
        };
    }
}
