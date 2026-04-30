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
    public class AutoTransform : MediaFoundationTransform
    {
        public AutoTransform(IMFMediaType nativeType, int outputWidth, int outputHeight, int frameRate, bool hardwareAcceleration = true)
        {
            HResult hr;

            IMFMediaType outputMediaType;
            hr = MFExtern.MFCreateMediaType(out outputMediaType);
            MFError.ThrowExceptionForHR(hr);

            hr = nativeType.CopyAllItems(outputMediaType);
            MFError.ThrowExceptionForHR(hr);

            hr = MFExtern.MFSetAttributeSize(outputMediaType, MFAttributesClsid.MF_MT_FRAME_SIZE, outputWidth, outputHeight);
            MFError.ThrowExceptionForHR(hr);

            hr = MFExtern.MFSetAttributeRatio(outputMediaType, MFAttributesClsid.MF_MT_FRAME_RATE, frameRate, 1);
            MFError.ThrowExceptionForHR(hr);

            MFT_EnumFlag flags = MFT_EnumFlag.SyncMFT | MFT_EnumFlag.LocalMFT | MFT_EnumFlag.SortAndFilter;
            if (hardwareAcceleration)
                flags |= MFT_EnumFlag.Hardware;
            Initialise(MFTransformCategory.MFT_CATEGORY_VIDEO_PROCESSOR, nativeType, outputMediaType, flags);
            SafeRelease(outputMediaType);
        }
    }
}
