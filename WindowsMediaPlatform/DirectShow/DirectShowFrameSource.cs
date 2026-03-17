using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DirectShowLib;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Tools;
using ImageServer;

namespace WindowsMediaPlatform.DirectShow
{
    public abstract class DirectShowFrameSource : TextureFrameSource, ISampleGrabberCB
    {
        protected IFilterGraph2 filterGraph;
        protected IBaseFilter grabber;
        protected IBaseFilter smartTee;
        protected IBaseFilter colorSpaceConverter;

        private int width;
        private int height;
        private int stride;
        protected IMediaControl MediaControl { get; private set; }

        public int DroppedFrames { get; set; }

        public override int FrameHeight => height;
        public override int FrameWidth => width;
        public override SurfaceFormat FrameFormat => SurfaceFormat.Bgr32;

        private bool setup;

        protected DirectShowFrameSource(VideoConfig videoSource)
            :base(videoSource)
        {
            Tools.Logger.VideoLog.LogCall(this);
        }

        public virtual bool Setup()
        {
            setup = false;
            int hr;

            try
            {
                if (IsDisposed)
                {
                    throw new Exception();
                }

                filterGraph = new FilterGraph() as IFilterGraph2;
                MediaControl = (IMediaControl)filterGraph;

                smartTee = (IBaseFilter)new SmartTee();
                hr = filterGraph.AddFilter(smartTee, "SmartTee");
                DsError.ThrowExceptionForHR(hr);

                // Get the SampleGrabber interface
                ISampleGrabber sampGrabber = (ISampleGrabber)new SampleGrabber();
                grabber = (IBaseFilter)sampGrabber;

                hr = filterGraph.AddFilter(grabber, "SampleGrabber");
                DsError.ThrowExceptionForHR(hr);

                //Setup the sample grabber.
                {
                    AMMediaType media;
                    // Set the media type to Video.
                    media = new AMMediaType();
                    media.majorType = MediaType.Video;
                    media.formatType = FormatType.VideoInfo;
                    hr = sampGrabber.SetMediaType(media);
                    DsError.ThrowExceptionForHR(hr);

                    DsUtils.FreeAMMediaType(media);
                    media = null;

                    // Configure the samplegrabber
                    hr = sampGrabber.SetCallback(this, 1);
                    DsError.ThrowExceptionForHR(hr);
                }

                // Setup the source.
                {
                    hr = SetupSource();
                    DsError.ThrowExceptionForHR(hr);
                }

                IPin[] connectedPins = new IPin[]
                {
                    smartTee.GetPin(PinDirection.Output),
                    grabber.GetPin(PinDirection.Input)
                };

                // Connect the Smart Tee to the samplegrabber
                hr = filterGraph.ConnectPin(smartTee, grabber);
                DsError.ThrowExceptionForHR(hr);

                //Set the media type.
                {
                    AMMediaType media = new AMMediaType();
                    hr = sampGrabber.GetConnectedMediaType(media);
                    DsError.ThrowExceptionForHR(hr);

                    if ((media.formatType != FormatType.VideoInfo) || (media.formatPtr == IntPtr.Zero))
                    {
                        throw new NotSupportedException("Unknown Grabber Media Format");
                    }

                    // Grab the size info
                    VideoInfoHeader videoInfoHeader = (VideoInfoHeader)Marshal.PtrToStructure(media.formatPtr, typeof(VideoInfoHeader));
                    width = videoInfoHeader.BmiHeader.Width;
                    height = videoInfoHeader.BmiHeader.Height;
                    stride = width * (videoInfoHeader.BmiHeader.BitCount / 8);
                    string format = MediaFoundation.MFHelper.GetFormat(media.subType);

                    //Disconnect existing connections
                    foreach (IPin pin in connectedPins)
                    {
                        hr = pin.Disconnect();
                        DsError.ThrowExceptionForHR(hr);
                    }

                    // Set the media type on our sample grabbing friend.
                    media.subType = MediaSubType.RGB32;
                    hr = sampGrabber.SetMediaType(media);
                    DsError.ThrowExceptionForHR(hr);

                    // Add colour space converter to the graph (easy conversion between various RGBs)
                    colorSpaceConverter = filterGraph.AddFilter(FilterCategory.LegacyAmFilterCategory, "Color Space Converter");

                    // Connect the Smart Tee to the colour space
                    hr = filterGraph.ConnectPin(smartTee, colorSpaceConverter);
                    DsError.ThrowExceptionForHR(hr);

                    // Connect the color converter to the samplegrabber
                    hr = filterGraph.ConnectPin(colorSpaceConverter, grabber);
                    DsError.ThrowExceptionForHR(hr);

                    hr = sampGrabber.GetConnectedMediaType(media);
                    DsError.ThrowExceptionForHR(hr);

                    videoInfoHeader = (VideoInfoHeader)Marshal.PtrToStructure(media.formatPtr, typeof(VideoInfoHeader));
                    width = videoInfoHeader.BmiHeader.Width;
                    height = videoInfoHeader.BmiHeader.Height;
                    stride = width * (videoInfoHeader.BmiHeader.BitCount / 8);

                    Tools.Logger.VideoLog.Log(this, "VideoInfoHeader", "Width: " + width + ", Height: " + height + ", Stride: " +  stride + ", Format: " + format);

                    DsUtils.FreeAMMediaType(media);
                    media = null;

                    rawTextures = new XBuffer<RawTexture>(5, width, height);
                    //try
                    //{
                    //    DirectShowLib.Utils.FilterGraphTools.SaveGraphFile(filterGraph, @"log\lastGraph.grf");
                    //}
                    //catch (Exception e) 
                    //{
                    //    Tools.Logger.VideoLog.LogException(this, e);
                    //}
                    Connected = true;
                    setup = true;
                }
            }
            catch (Exception ee)
            {
                Tools.Logger.VideoLog.LogException(this, ee);
                DirectShowLib.Utils.FilterGraphTools.SaveGraphFile(filterGraph, @"log\lastGraph.grf");
                Connected = false;
                setup = false;
            }

            return setup;
        }

        public virtual AMMediaType GetSourceMediaType()
        {
            return null;
        }

        public override void CleanUp()
        {
            base.CleanUp();
            setup = false;
            Tools.Logger.VideoLog.LogCall(this);

            if (filterGraph != null)
            {
                Marshal.ReleaseComObject(filterGraph);
                filterGraph = null;

                // This is just a different interface on the same object and needs to go.
                MediaControl = null;
            }

            if (grabber != null)
            {
                Marshal.ReleaseComObject(grabber);
                grabber = null;
            }

            if (colorSpaceConverter != null)
            {
                Marshal.ReleaseComObject(colorSpaceConverter);
                colorSpaceConverter = null;
            }

            if (smartTee != null)
            {
                Marshal.ReleaseComObject(smartTee);
                smartTee = null;
            }
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogCall(this);
            if (!setup)
            {
                Setup();

                if (!setup)
                {
                    return false;
                }
            }

            int hr;
            
            try
            {
                hr = MediaControl.Run();

                if (hr == -2147023446)
                {
                    Connected = false;
                    throw new InsufficientResourcesException();
                }
                else
                {
                    DsError.ThrowExceptionForHR(hr);
                }
            }
            catch (Exception e)
            {
                Tools.Logger.VideoLog.LogException(this, e);
                return false;
            }

            return base.Start();
        }

        protected virtual int SetupSource()
        {
            return 0;
        }

        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogCall(this);
            
            if (MediaControl != null)
            {
                MediaControl.Stop();
            }

            return base.Stop();
        }

        public int SampleCB(double sampleTime, IMediaSample sample)
        {
            Marshal.ReleaseComObject(sample);
            return 0;
        }

        public virtual int BufferCB(double sampleTime, IntPtr buffer, int bufferLen)
        {
            var currentRawTextures = rawTextures;
            if (currentRawTextures != null)
            {
                RawTexture frame;

                if (rawTextures.GetWritable(out frame))
                {
                    FrameProcessNumber++;

                    long sampleTicks = (long)(sampleTime * 10000000);

                    frame.SetData(buffer, sampleTicks, FrameProcessNumber);
                    rawTextures.WriteOne(frame);
                }
                NotifyReceivedFrame();
            }

            return 0;
        }

        public override bool Pause()
        {
            if (MediaControl != null)
            {
                int hr = MediaControl.Stop();
                
                Tools.Logger.VideoLog.LogCall(this, VideoConfig.DeviceName, hr);
               
                if (hr != 0)
                    return false;

            }

            return base.Pause();
        }

        public override bool Unpause()
        {
            if (MediaControl != null)
            {
                int hr = MediaControl.Run();

                Tools.Logger.VideoLog.LogCall(this, VideoConfig.DeviceName, hr);

                if (hr != 0)
                    return false;
            }

            return base.Unpause();
        }
    }
}
