using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.Transform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class WMVEncoder : MediaFoundationTransform
    {
        private Guid iidIWMCodecPrivateData = new Guid("73F0BE8E-57F7-4f01-AA66-9F57340CFE0E");
        private Guid propertyStore = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

        public WMVEncoder(IMFMediaType nativeType, int width, int height, int frameRate)
            : base()
        {
            IMFMediaType encoderOutput = null;

            try
            {
                HResult hr = HResult.S_OK;

                MFTRegisterTypeInfo inType = new MFTRegisterTypeInfo();
                nativeType.GetGUID(MFAttributesClsid.MF_MT_MAJOR_TYPE, out inType.guidMajorType);
                nativeType.GetGUID(MFAttributesClsid.MF_MT_SUBTYPE, out inType.guidSubtype);

                MFTRegisterTypeInfo outType = new MFTRegisterTypeInfo();
                outType.guidMajorType = inType.guidMajorType;
                outType.guidSubtype = MFMediaType.WMV3;

                IMFTransform encoder = MFHelper.CreateTransform(MFTransformCategory.MFT_CATEGORY_VIDEO_ENCODER, MFT_EnumFlag.SortAndFilter, inType, outType);

                IPropertyStore propertyStore;
                hr = MFExtern.CreatePropertyStore(out propertyStore);
                MFError.ThrowExceptionForHR(hr);

                hr = encoder.SetInputType(0, nativeType, MFTSetTypeFlags.None);

                int i = 0;
                while (Succeeded(hr))
                {
                    hr = encoder.GetOutputAvailableType(0, i, out encoderOutput);
                    Console.WriteLine(MFDump.DumpAttribs(encoderOutput));
                    i++;

                    SafeRelease(encoderOutput);
                }

                hr = encoder.GetOutputAvailableType(0, 0, out encoderOutput);

                int bitRate = GetBitRate(width, height, frameRate);
                hr = encoderOutput.SetUINT32(MFAttributesClsid.MF_MT_AVG_BITRATE, bitRate);
                MFError.ThrowExceptionForHR(hr);

                hr = MFExtern.MFSetAttributeSize(encoderOutput, MFAttributesClsid.MF_MT_FRAME_SIZE, width, height);
                MFError.ThrowExceptionForHR(hr);

                AMMediaType amMediaType = new AMMediaType();
                MFExtern.MFInitAMMediaTypeFromMFMediaType(encoderOutput, MFRepresentation.VideoInfo, amMediaType);

                IntPtr encoderPtr = Marshal.GetComInterfaceForObject(encoder, typeof(IMFTransform));
                IntPtr codecPrivateDataPtr;
                Marshal.QueryInterface(encoderPtr, ref iidIWMCodecPrivateData, out codecPrivateDataPtr);
                IWMCodecPrivateData codecPrivateData = (IWMCodecPrivateData)Marshal.GetObjectForIUnknown(codecPrivateDataPtr);

                codecPrivateData.SetPartialOutputType(amMediaType);

                int length;
                hr = codecPrivateData.GetPrivateData(IntPtr.Zero, out length);
                MFError.ThrowExceptionForHR(hr);

                IntPtr privateDataPtr = Marshal.AllocCoTaskMem(length);
                codecPrivateData.GetPrivateData(privateDataPtr, out length);

                byte[] blob = new byte[length];
                Marshal.Copy(privateDataPtr, blob, 0, length);

                hr = encoderOutput.SetBlob(MFAttributesClsid.MF_MT_USER_DATA, blob, length);
                MFError.ThrowExceptionForHR(hr);

                Console.WriteLine(MFDump.DumpAttribs(nativeType));
                Console.WriteLine(MFDump.DumpAttribs(encoderOutput));

                string format = MFHelper.GetFormat(outType.guidSubtype);
                Tools.Logger.VideoLog.Log(this, "WMVEncoder", MFHelper.GetFormat(inType.guidSubtype) + " to Width: " + width + " Height: " + height + " Format: " + format);

                hr = encoder.SetOutputType(0, encoderOutput, MFTSetTypeFlags.None);
                MFError.ThrowExceptionForHR(hr);

                Initialise(encoder);
            }
            finally
            {
                SafeRelease(encoderOutput);
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
    }

    [ComImport, System.Security.SuppressUnmanagedCodeSecurity,
    Guid("73F0BE8E-57F7-4f01-AA66-9F57340CFE0E"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWMCodecPrivateData
    {
        [PreserveSig]
        HResult SetPartialOutputType(
                    AMMediaType pmt);

        [PreserveSig]
        HResult GetPrivateData(
                    IntPtr pbData,
                    out int pcbData);
    }
}
