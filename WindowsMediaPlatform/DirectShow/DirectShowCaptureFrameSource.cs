using DirectShowLib;
using DirectShowLib.GDCL;
using ImageServer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Tools;

namespace WindowsMediaPlatform.DirectShow
{
    public class DirectShowCaptureFrameSource : DirectShowDeviceFrameSource, ICaptureFrameSource
    {
        public string Filename { get; set; }

        private IGMFBridgeController gmfBridgeController;

        private IBaseFilter sourceSink;

        private DestinationGraph destinationGraph;

        public FrameTime[] FrameTimes
        {
            get
            {
                if (destinationGraph != null)
                {
                    return destinationGraph.FrameTimes;
                }
                return new FrameTime[0];    
            }
        }

        public bool RecordNextFrameTime
        {
            set
            {
                if (destinationGraph != null)
                {
                    destinationGraph.RecordNextFrameTime = value;
                }
            }
        }

        public bool ManualRecording { get; set; }

        public bool Finalising
        {
            get
            {
                return false;
            }
        }

        public DirectShowCaptureFrameSource(VideoConfig videoSource)
            : base(videoSource)
        {
        }

        public void StartRecording(string filename)
        {
            int height = VideoConfig.RecordResolution;
            int fps = VideoConfig.RecordFrameRate;

            Logger.VideoLog.LogCall(this);
            try
            {
                if (destinationGraph != null)
                {
                    destinationGraph.Dispose();
                    destinationGraph = null;
                }

                if (gmfBridgeController == null)
                    return;

                height = Math.Min(Height, height);
                fps = Math.Max(10, fps);

                int width = GetWidth(height);
                int avgTimePerFrame = 10000000 / fps;

                avgTimePerFrame = Math.Max(avgTimePerFrame, AverageTimePerFrame);

                string audioDevicePath = GetAudioDevicePath();

                if (audioDevicePath == null)
                {
                    throw new MissingAudioDeviceException();
                }

                destinationGraph = new DestinationGraph(gmfBridgeController, sourceSink, audioDevicePath, filename, width, height, avgTimePerFrame);
                destinationGraph.Start();

                Filename = destinationGraph.Filename;
                Recording = true;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                destinationGraph = null;
                Recording = false;
            }
        }

        private string GetAudioDevicePath()
        {
            DsDevice[] audioDevices = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice);

            var ordered = audioDevices.Where(a => a != null && a.Name != null).OrderByDescending(a => Maths.LevenshteinDistance(a.Name, VideoConfig.AudioDevice));

            DsDevice chosenAudio = ordered.FirstOrDefault();
            string devicePath = null;
            if (chosenAudio != null)
            {
                devicePath = chosenAudio.DevicePath;
            }
            foreach (DsDevice audioDevice in audioDevices)
            {
                if (audioDevice == null)
                {
                    audioDevice.Dispose();
                }
            }
           
            return devicePath;
        } 

        private int GetWidth(int targetHeight)
        {
            double[] validAspects = new double[] 
            { 
                4.0 / 3.0,
                16.0 / 9.0
            };

            double thisAspect = Width / (double)Height;
            thisAspect = validAspects.OrderBy(n => Math.Abs(n - thisAspect)).First();

            int width = (int)(thisAspect * targetHeight);

            // hack to to do rounding
            width /= 10;
            width *= 10;

            return width;
        }

        public void StopRecording()
        {
            Logger.VideoLog.LogCall(this);

            try
            {
                if (destinationGraph != null)
                {
                    destinationGraph.Dispose();
                    destinationGraph = null;
                }
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                destinationGraph = null;
            }
            Recording = false;
        }

        protected override int SetupSource()
        {
            int hr = base.SetupSource();
            
            gmfBridgeController = (IGMFBridgeController)new GMFBridgeController();

            // init to video-only, in discard mode (ie when source graph
            // is running but not connected, buffers are discarded at the bridge)
            hr = gmfBridgeController.AddStream(true, eFormatType.MuxInputs, true);
            DsError.ThrowExceptionForHR(hr);

            // Add the sink filter to the source graph
            hr = gmfBridgeController.InsertSinkFilter(filterGraph, out sourceSink);
            DsError.ThrowExceptionForHR(hr);

            //Setup video pin
            hr = filterGraph.ConnectPin(smartTee, sourceSink);
            DsError.ThrowExceptionForHR(hr);

            return hr;
        }

        public override void CleanUp()
        {
            if (destinationGraph != null)
            {
                destinationGraph.Dispose();
                destinationGraph = null;
            }

            base.CleanUp();

          
            if (gmfBridgeController != null)
            {
                Marshal.ReleaseComObject(gmfBridgeController);
                gmfBridgeController = null;
            }

            if (sourceSink != null)
            {
                Marshal.ReleaseComObject(sourceSink);
                sourceSink = null;
            }
        }

        public override bool Pause()
        {
            if (Recording)
            {
                return false;
            }

            return base.Pause();
        }
    }

    public class MissingAudioDeviceException : Exception
    {
        public MissingAudioDeviceException()
            :base("Missing Audio Device - Can't record video without audio.")
        {

        }
    }

}
