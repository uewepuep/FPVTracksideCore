using DirectShowLib;
using DirectShowLib.GDCL;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Tools;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Collections.Generic;
using ImageServer;

namespace WindowsMediaPlatform.DirectShow
{
    class DestinationGraph : IDisposable, ISampleGrabberCB
    {
        private IGMFBridgeController gmfBridgeController;

        private IBaseFilter destSource;
        private IFilterGraph2 filterGraph;
        private ICaptureGraphBuilder2 capGraph;

        private IFileSinkFilter fileSink;
        private IBaseFilter fileOutFilter;
        private IBaseFilter audioFilter;

        private IBaseFilter sourceSink;
        private IBaseFilter grabber;

        public string Filename { get; private set; }

        public FrameTime[] FrameTimes
        {
            get
            {
                lock (frameTimes)
                {
                    return frameTimes.ToArray();
                }
            }
        }

        private List<FrameTime> frameTimes;

        private IMediaControl MediaControl { get { return filterGraph as IMediaControl; } }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int AverageTimePerFrame { get; private set; }

        private string audioDevicePath;

        public bool RecordNextFrameTime { get; set; }

        private int frameCount;

        public DestinationGraph(IGMFBridgeController gmfBridgeController, IBaseFilter sourceSink, string audioDevicePath, string filename, int width, int height, int avgTimePerFrame)
        {
            this.audioDevicePath = audioDevicePath;
            Filename = filename;
            this.gmfBridgeController = gmfBridgeController;
            this.sourceSink = sourceSink;
            frameTimes = new List<FrameTime>();

            if (File.Exists(filename))
            {
                File.Delete(filename);
            }

            Width = width;
            Height = height;
            AverageTimePerFrame = avgTimePerFrame;
            RecordNextFrameTime = true;
            frameCount = 0;
        }

        private void BuildGraph()
        {
            int hr;
            filterGraph = (IFilterGraph2)new FilterGraph();

            // Use the bridge to add the sourcefilter to the graph
            hr = gmfBridgeController.InsertSourceFilter(sourceSink, filterGraph, out destSource);
            DsError.ThrowExceptionForHR(hr);

            // use capture graph builder to create mux/writer stage
            capGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

            hr = capGraph.SetFiltergraph(filterGraph);
            DsError.ThrowExceptionForHR(hr);

            if (audioFilter == null && audioDevicePath != null)
            {
                DsDevice device = DirectShowHelper.GetAudioCaptureDevice(audioDevicePath);
                if (device == null)
                {
                    throw new NullReferenceException("Couldn't find device " + audioDevicePath);
                }

                hr = filterGraph.AddSourceFilterForMoniker(device.Mon, null, "audio", out audioFilter);
                DsError.ThrowExceptionForHR(hr);

                if (device == null)
                {
                    device.Dispose();
                }
            }

            // Get the SampleGrabber interface
            ISampleGrabber sampGrabber = (ISampleGrabber)new SampleGrabber();
            grabber = (IBaseFilter)sampGrabber;

            hr = filterGraph.AddFilter(grabber, "SampleGrabber");
            DsError.ThrowExceptionForHR(hr);

            // Connect the source to the samplegrabber
            hr = filterGraph.ConnectPin(destSource, grabber);
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

            hr = ConfigureWMV(grabber, audioFilter);
            DsError.ThrowExceptionForHR(hr);
        }

        public void Start()
        {
            BuildGraph();

            int hr;
            // start capture graph
            hr = MediaControl.Run();
            DsError.ThrowExceptionForHR(hr);

            hr = gmfBridgeController.BridgeGraphs(sourceSink, destSource);
            DsError.ThrowExceptionForHR(hr);
        }

        private int ConfigureWMV(IBaseFilter videoIn, IBaseFilter audioIn)
        {
            int hr;

            if (!Filename.EndsWith("wmv"))
            {
                Filename += ".wmv";
            }


            hr = capGraph.SetOutputFileName(MediaSubType.Asf, Filename, out fileOutFilter, out fileSink);
            DsError.ThrowExceptionForHR(hr);

            WindowsMediaLib.IWMProfileManager profileManager;
            WindowsMediaLib.WMUtils.WMCreateProfileManager(out profileManager);

            //Setup audio pin
            hr = filterGraph.ConnectPin(audioIn, fileOutFilter);
            DsError.ThrowExceptionForHR(hr);

            //Setup video pin
            hr = filterGraph.ConnectPin(videoIn, fileOutFilter);
            DsError.ThrowExceptionForHR(hr);

            //WindowsMediaLib.IWMProfile profile;
            //Guid hqwmv = new Guid("F6A5F6DF-EE3F-434C-A433-523CE55F516B");
            //profileManager.LoadProfileByID(hqwmv, out profile);

            //int reffer = 100000;
            //StringBuilder sb = new StringBuilder(reffer);
            //profileManager.SaveProfile(profile, sb, ref reffer);
            //string s = sb.ToString();

            string profileText = GetTextFile(@"WindowsMediaPlatform.wmvHQ.prx");
            profileText = profileText.Replace("$width", Width.ToString());
            profileText = profileText.Replace("$height", Height.ToString());

            profileText = profileText.Replace("$avgtimeperframe", AverageTimePerFrame.ToString());

            long bitRate = GetBitRate(Width, Height, AverageTimePerFrame);
            profileText = profileText.Replace("$bitrate", bitRate.ToString());

            Logger.VideoLog.LogCall(this, Filename, Width, Height, AverageTimePerFrame, bitRate);

            WindowsMediaLib.IWMProfile profile;
            profileManager.LoadProfileByData(profileText, out profile);

            WindowsMediaLib.IConfigAsfWriter icaw = (WindowsMediaLib.IConfigAsfWriter)fileOutFilter;
            icaw.ConfigureFilterUsingProfile(profile);
            return hr;
        }

        private struct Size
        {
            public int Width { get; set; }
            public int Height { get; set; }

            public Size(int w, int h) { Width = w; Height = h; }
        }

        private static long GetBitRate(int width, int height, long averagetimeperframe)
        {
            const double pixelToBitrateConstant = 12000000.0 / ((1280 * 720) / 333333.0f);

            double ratio = (width * height) / (double)averagetimeperframe;

            return (long)(ratio * pixelToBitrateConstant);
        }

        private int ConfigureAVI(string outputPath, IBaseFilter videoIn, IBaseFilter audioIn)
        {
            int hr;

            hr = capGraph.SetOutputFileName(MediaSubType.Avi, outputPath + ".avi", out fileOutFilter, out fileSink);
            DsError.ThrowExceptionForHR(hr);
            Marshal.ReleaseComObject(fileSink);

            //Setup audio pin
            hr = filterGraph.ConnectPin(audioIn, fileOutFilter);
            DsError.ThrowExceptionForHR(hr);

            //Setup video pin
            hr = filterGraph.ConnectPin(videoIn, fileOutFilter);
            DsError.ThrowExceptionForHR(hr);

            return hr;
        }


        public void Dispose()
        {
            Logger.VideoLog.LogCall(this, Filename);

            if (gmfBridgeController != null)
            {
                gmfBridgeController.BridgeGraphs(null, null);
            }

            if (MediaControl != null)
            {
                MediaControl.Stop();
            }

            if (audioFilter != null)
            {
                Marshal.ReleaseComObject(audioFilter);
                audioFilter = null;
            }

            if (destSource != null)
            {
                Marshal.ReleaseComObject(destSource);
                destSource = null;
            }

            if (grabber != null)
            {
                Marshal.ReleaseComObject(grabber);
                grabber = null;
            }

            if (fileSink != null)
            {
                Marshal.ReleaseComObject(fileSink);
                fileSink = null;
            }

            if (fileOutFilter != null)
            {
                Marshal.ReleaseComObject(fileOutFilter);
                fileOutFilter = null;
            }

            if (filterGraph != null)
            {
                Marshal.ReleaseComObject(filterGraph);
                filterGraph = null;
            }

            if (capGraph != null)
            {
                Marshal.ReleaseComObject(capGraph);
                capGraph = null;
            }
        }

        public int SampleCB(double sampleTime, IMediaSample sample)
        {
            Marshal.ReleaseComObject(sample);
            return 0;
        }

        public int BufferCB(double sampleTime, IntPtr buffer, int bufferLen)
        {
            frameCount++;

            if (RecordNextFrameTime)
            {
                lock (frameTimes)
                {
                    frameTimes.Add(new FrameTime() { Frame = frameCount, Time = DateTime.Now, Seconds = sampleTime });
                }
                RecordNextFrameTime = false;
            }
            return 0;
        }

        private string GetTextFile(string name)
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetCallingAssembly();
            string[] names = assembly.GetManifestResourceNames();
            using (Stream resourceStream = assembly.GetManifestResourceStream(name))
            {
                using (StreamReader sr = new StreamReader(resourceStream))
                {
                    return sr.ReadToEnd();
                }
            }
        }
    }
}
