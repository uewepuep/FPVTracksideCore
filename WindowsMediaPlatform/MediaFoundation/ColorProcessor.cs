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
    public class ColorProcessor : MediaFoundationTransform
    {
        public static IEnumerable<Guid> SupportedInputTypes()
        {
            return new Guid[]
            {
                MFMediaType.ARGB32,
                MFMediaType.AYUV,
                MFMediaType.I420,
                MFMediaType.IYUV,
                MFMediaType.NV11,
                MFMediaType.NV12,
                MFMediaType.RGB24,
                MFMediaType.RGB32,
                MFMediaType.RGB555,
                MFMediaType.RGB565,
                MFMediaType.RGB8,
                MFMediaType.UYVY,
                MFMediaType.v410,
                MFMediaType.Y216,
                MFMediaType.Y41P,
                MFMediaType.Y41T,
                MFMediaType.Y42T,
                MFMediaType.YUY2,
                MFMediaType.YV12
            };
        }

        private IMFMediaType destinationType;
        public IMFMediaType DestinationType { get { return destinationType; } }

        public ColorProcessor(IMFMediaType sourceType, Guid destinationSubType, bool hardwareAcceleration = true)
        {
            HResult hr = HResult.S_OK;

            destinationType = null;
            hr = MFExtern.MFCreateMediaType(out destinationType);
            MFError.ThrowExceptionForHR(hr);

            hr = sourceType.CopyAllItems(destinationType);
            MFError.ThrowExceptionForHR(hr);

            hr = destinationType.SetGUID(MFAttributesClsid.MF_MT_SUBTYPE, destinationSubType);
            MFError.ThrowExceptionForHR(hr);

            MFT_EnumFlag flags = MFT_EnumFlag.SyncMFT | MFT_EnumFlag.LocalMFT | MFT_EnumFlag.SortAndFilter;
            if (hardwareAcceleration)
                flags |= MFT_EnumFlag.Hardware;
            Initialise(MFTransformCategory.MFT_CATEGORY_VIDEO_PROCESSOR, sourceType, destinationType, flags);
        }

        public override void Dispose()
        {
            base.Dispose();

            if (destinationType != null)
            {
                SafeRelease(destinationType);
                destinationType = null;
            }
        }
    }
}
