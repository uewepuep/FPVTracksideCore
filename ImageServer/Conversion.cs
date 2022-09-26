using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ImageServer
{

    [StructLayout(LayoutKind.Sequential)]
    public struct YUYV
    {
        public byte Y;
        public byte U;
        public byte Y2;
        public byte V;

        public YUYV(byte y, byte u, byte y2, byte v)
        {
            Y = y;
            U = u;
            Y2 = y2;
            V = v;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RGBQUAD
    {
        public byte B;
        public byte G;
        public byte R;
        public byte A;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RGB24
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
    }

    public static class Conversion
    {
        //public static Dictionary<Guid,string> RGBFormats = new Dictionary<Guid, string>()
        //{
        //    { DirectShowLib.MediaSubType.RGB8, "RGB8" },
        //    { DirectShowLib.MediaSubType.RGB555, "RGB555" },
        //    { DirectShowLib.MediaSubType.RGB565, "RGB565" },
        //    { DirectShowLib.MediaSubType.RGB24, "RGB24" },
        //    { DirectShowLib.MediaSubType.RGB32, "RGB32" },
        //    { DirectShowLib.MediaSubType.MJPG, "MJPG" }
        //};


        private static byte Clip(int clr)
        {
            return (byte)(clr < 0 ? 0 : (clr > 255 ? 255 : clr));
        }

        private static RGBQUAD ConvertYCrCbToRGB(byte y, byte cr, byte cb)
        {
            RGBQUAD rgbq = new RGBQUAD();

            int c = y - 16;
            int d = cb - 128;
            int e = cr - 128;

            rgbq.R = Clip((298 * c + 409 * e + 128) >> 8);
            rgbq.G = Clip((298 * c - 100 * d - 208 * e + 128) >> 8);
            rgbq.B = Clip((298 * c + 516 * d + 128) >> 8);

            return rgbq;
        }


        ////-------------------------------------------------------------------
        //// TransformImage_RGB24
        ////
        //// RGB-24 to RGB-32
        ////-------------------------------------------------------------------
        //unsafe private static void TransformImage_RGB24(byte* pDest, int lDestStride, byte* pSrc, int lSrcStride, int dwWidthInPixels, int dwHeightInPixels)
        //{
        //    RGB24* source = (RGB24*)pSrc;
        //    RGBQUAD* dest = (RGBQUAD*)pDest;

        //    lSrcStride /= 3;
        //    lDestStride /= 4;

        //    for (int y = 0; y < dwHeightInPixels; y++)
        //    {
        //        for (int x = 0; x < dwWidthInPixels; x++)
        //        {
        //            dest[x].R = source[x].rgbRed;
        //            dest[x].G = source[x].rgbGreen;
        //            dest[x].B = source[x].rgbBlue;
        //            dest[x].A = 0;
        //        }

        //        source += lSrcStride;
        //        dest += lDestStride;
        //    }
        //}

        ////-------------------------------------------------------------------
        //// TransformImage_RGB32
        ////
        //// RGB-32 to RGB-32
        ////
        //// Note: This function is needed to copy the image from system
        //// memory to the Direct3D surface.
        ////-------------------------------------------------------------------
        //unsafe public static void TransformImage_RGB32(byte* pDest, int lDestStride, byte* pSrc, int lSrcStride, int dwWidthInPixels, int dwHeightInPixels)
        //{
        //    Buffer.MemoryCopy(pSrc, pDest, dwHeightInPixels * dwWidthInPixels * 4, dwHeightInPixels * dwWidthInPixels * 4);
        //}

        ////-------------------------------------------------------------------
        //// TransformImage_YUY2
        ////
        //// YUY2 to RGB-32
        ////-------------------------------------------------------------------
        //unsafe public static void TransformImage_YUY2(byte* pDest, int lDestStride, byte* pSrc, int lSrcStride, int dwWidthInPixels, int dwHeightInPixels)
        //{
        //    YUYV* pSrcPel = (YUYV*)pSrc;
        //    RGBQUAD* pDestPel = (RGBQUAD*)pDest;

        //    lSrcStride /= 4; // convert lSrcStride to YUYV
        //    lDestStride /= 4; // convert lDestStride to RGBQUAD

        //    for (int y = 0; y < dwHeightInPixels; y++)
        //    {
        //        for (int x = 0; x < dwWidthInPixels / 2; x++)
        //        {
        //            pDestPel[x * 2] = ConvertYCrCbToRGB(pSrcPel[x].Y, pSrcPel[x].V, pSrcPel[x].U);
        //            pDestPel[(x * 2) + 1] = ConvertYCrCbToRGB(pSrcPel[x].Y2, pSrcPel[x].V, pSrcPel[x].U);
        //        }

        //        pSrcPel += lSrcStride;
        //        pDestPel += lDestStride;
        //    }
        //}


        ////-------------------------------------------------------------------
        //// TransformImage_NV12
        ////
        //// NV12 to RGB-32
        ////-------------------------------------------------------------------
        //unsafe public static void TransformImage_NV12(byte* pDest, int lDestStride, byte* pSrc, int lSrcStride, int dwWidthInPixels, int dwHeightInPixels)
        //{
        //    Byte* lpBitsY = (byte*)pSrc;
        //    Byte* lpBitsCb = lpBitsY + (dwHeightInPixels * lSrcStride);
        //    Byte* lpBitsCr = lpBitsCb + 1;

        //    Byte* lpLineY1;
        //    Byte* lpLineY2;
        //    Byte* lpLineCr;
        //    Byte* lpLineCb;

        //    Byte* lpDibLine1 = (Byte*)pDest;
        //    for (UInt32 y = 0; y < dwHeightInPixels; y += 2)
        //    {
        //        lpLineY1 = lpBitsY;
        //        lpLineY2 = lpBitsY + lSrcStride;
        //        lpLineCr = lpBitsCr;
        //        lpLineCb = lpBitsCb;

        //        Byte* lpDibLine2 = lpDibLine1 + lDestStride;

        //        for (UInt32 x = 0; x < dwWidthInPixels; x += 2)
        //        {
        //            byte y0 = lpLineY1[0];
        //            byte y1 = lpLineY1[1];
        //            byte y2 = lpLineY2[0];
        //            byte y3 = lpLineY2[1];
        //            byte cb = lpLineCb[0];
        //            byte cr = lpLineCr[0];

        //            RGBQUAD r = ConvertYCrCbToRGB(y0, cr, cb);
        //            lpDibLine1[0] = r.B;
        //            lpDibLine1[1] = r.G;
        //            lpDibLine1[2] = r.R;
        //            lpDibLine1[3] = 0; // Alpha

        //            r = ConvertYCrCbToRGB(y1, cr, cb);
        //            lpDibLine1[4] = r.B;
        //            lpDibLine1[5] = r.G;
        //            lpDibLine1[6] = r.R;
        //            lpDibLine1[7] = 0; // Alpha

        //            r = ConvertYCrCbToRGB(y2, cr, cb);
        //            lpDibLine2[0] = r.B;
        //            lpDibLine2[1] = r.G;
        //            lpDibLine2[2] = r.R;
        //            lpDibLine2[3] = 0; // Alpha

        //            r = ConvertYCrCbToRGB(y3, cr, cb);
        //            lpDibLine2[4] = r.B;
        //            lpDibLine2[5] = r.G;
        //            lpDibLine2[6] = r.R;
        //            lpDibLine2[7] = 0; // Alpha

        //            lpLineY1 += 2;
        //            lpLineY2 += 2;
        //            lpLineCr += 2;
        //            lpLineCb += 2;

        //            lpDibLine1 += 8;
        //            lpDibLine2 += 8;
        //        }

        //        pDest += (2 * lDestStride);
        //        lpBitsY += (2 * lSrcStride);
        //        lpBitsCr += lSrcStride;
        //        lpBitsCb += lSrcStride;
        //    }
        //}
    }
}
