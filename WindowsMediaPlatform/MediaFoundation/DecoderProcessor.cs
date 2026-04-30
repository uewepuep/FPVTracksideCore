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
    public class DecoderProcessor : MediaFoundationTransform
    {
        public static IEnumerable<Guid> SupportedInputTypes()
        {
            return new Guid[]
            {
                MFMediaType.MJPG,
                MFMediaType.H264,
                MFMediaType.WMV1,
                MFMediaType.WMV2,
                MFMediaType.WMV3
            };
        }

        private IMFMediaType destinationType;
        public IMFMediaType DestinationType { get { return destinationType; } }

        public DecoderProcessor(IMFMediaType sourceType, Guid destinationSubType, bool hardwareAcceleration = true)
        {
            SingleSample = false;

            HResult hr = HResult.S_OK;

            hr = MFExtern.MFCreateMediaType(out destinationType);
            MFError.ThrowExceptionForHR(hr);

            hr = sourceType.CopyAllItems(destinationType);
            MFError.ThrowExceptionForHR(hr);

            hr = destinationType.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, destinationSubType);
            MFError.ThrowExceptionForHR(hr);

            MFT_EnumFlag flags = MFT_EnumFlag.SyncMFT | MFT_EnumFlag.LocalMFT | MFT_EnumFlag.SortAndFilter;
            if (hardwareAcceleration)
                flags |= MFT_EnumFlag.Hardware;
            Initialise(MFTransformCategory.MFT_CATEGORY_VIDEO_DECODER, sourceType, destinationType, flags, false);
        }

        public override void Dispose()
        {
            base.Dispose();
            SafeRelease(destinationType);
        }
    }
}
