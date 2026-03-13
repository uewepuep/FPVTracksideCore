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
    public class WMVPassthrough : MediaFoundationTransform
    {
        private IMFMediaType outputType;

        public WMVPassthrough(IMFMediaType pType, int width, int height, int frameRate)
           : base()
        {
            HResult hr = MFExtern.MFTRegisterLocalByCLSID(
                    typeof(CColorConvertDMO).GUID,
                    MFTransformCategory.MFT_CATEGORY_VIDEO_PROCESSOR,
                    "",
                    MFT_EnumFlag.SyncMFT,
                    0,
                    null,
                    0,
                    null
                    );
            MFError.ThrowExceptionForHR(hr);

            int bitRate = GetBitRate(width, height, frameRate);

            hr = MFExtern.MFCreateMediaType(out outputType);

            if (Succeeded(hr))
            {
                hr = outputType.SetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, MFMediaType.Video);
            }

            if (Succeeded(hr))
            {
                hr = outputType.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, MFMediaType.WMV3);
            }

            if (Succeeded(hr))
            {
                hr = outputType.SetUINT32(MFAttributesClsid.MF_MT_AVG_BITRATE, bitRate);
            }

            if (Succeeded(hr))
            {
                hr = CopyAttribute(pType, outputType, MFAttributesClsid.MF_MT_FRAME_SIZE);
            }

            if (Succeeded(hr))
            {
                hr = CopyAttribute(pType, outputType, MFAttributesClsid.MF_MT_FRAME_RATE);
            }

            if (Succeeded(hr))
            {
                hr = CopyAttribute(pType, outputType, MFAttributesClsid.MF_MT_PIXEL_ASPECT_RATIO);
            }

            if (Succeeded(hr))
            {
                hr = CopyAttribute(pType, outputType, MFAttributesClsid.MF_MT_INTERLACE_MODE);
            }
        }

        public override void Dispose()
        {
            SafeRelease(outputType);
            outputType = null;
            base.Dispose();
        }

        public override IMFMediaType GetOutputCurrentType()
        {
            IMFMediaType pType2 = null;
            HResult hr = MFExtern.MFCreateMediaType(out pType2);

            outputType.CopyAllItems(pType2);

            return pType2;
        }

        public override HResult ProcessInput(IMFSample sample)
        {
            Output(sample);
            return HResult.S_OK;
        }

        HResult CopyAttribute(IMFAttributes pSrc, IMFAttributes pDest, Guid key)
        {
            PropVariant var = new PropVariant();

            HResult hr = HResult.S_OK;

            hr = pSrc.GetItem(key, var);
            if (Succeeded(hr))
            {
                hr = pDest.SetItem(key, var);
            }

            return hr;
        }

        private static int GetBitRate(int width, int height, int frameRate)
        {
            int goodBitrate = 12000000;
            int goodPixelCount = 1280 * 720 * 30;
            float ratio = goodBitrate / (float)goodPixelCount;

            int thisPixelCount = width * height * frameRate;

            return (int)(ratio * thisPixelCount);
        }
    }
}
