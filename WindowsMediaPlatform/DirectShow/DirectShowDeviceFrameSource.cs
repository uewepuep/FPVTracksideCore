using DirectShowLib;
using ImageServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.DirectShow
{
    public class DirectShowDeviceFrameSource : DirectShowFrameSource, IHasModes
    {
        public int Width { get { return VideoConfig.VideoMode.Width; } }
        public int Height { get { return VideoConfig.VideoMode.Height; } }
        public float FrameRate { get { return VideoConfig.VideoMode.FrameRate; } }
        public int AverageTimePerFrame { get; private set; }

        public string DeviceName { get { return VideoConfig.DeviceName; } }
        public string Path { get { return VideoConfig.DirectShowPath; } }

        public Guid SubType { get; private set; }

        protected IBaseFilter capFilter;
        protected ICaptureGraphBuilder2 capGraph;
        private Mode[] modes;

        public DirectShowDeviceFrameSource(VideoConfig videoSource)
            : base(videoSource)
        {
            Tools.Logger.VideoLog.LogCall(this, videoSource.DeviceName, Width, Height, FrameRate);
        }

        public override void CleanUp()
        {
            if (capFilter != null)
            {
                Marshal.ReleaseComObject(capFilter);
                capFilter = null;
            }

            if (capGraph != null)
            {
                Marshal.ReleaseComObject(capGraph);
                capGraph = null;
            }
            base.CleanUp();
        }

        protected IBaseFilter GetCapFilter()
        {
            IBaseFilter filter;

            DsDevice device = DirectShowHelper.GetVideoCaptureDeviceByPath(VideoConfig.DirectShowPath);
            if (device == null)
            {
                if (VideoConfig.AnyUSBPort)
                {
                    device = DirectShowHelper.GetVideoCaptureDeviceByName(VideoConfig.DeviceName);
                }

                if (device == null)
                {
                    return null;
                }
            }

            int hr = filterGraph.AddSourceFilterForMoniker(device.Mon, null, device.Name, out filter);
            DsError.ThrowExceptionForHR(hr);

            if (device != null)
            {
                device.Dispose();
            }

            return filter;
        }

        protected override int SetupSource()
        {
            int hr;
            capFilter = GetCapFilter();
            if (capFilter != null)
            {
                SetMode(VideoConfig.VideoMode);

                // Connect the graph.
                hr = capGraph.RenderStream(PinCategory.Capture, MediaType.Video, capFilter, null, smartTee);
                DsError.ThrowExceptionForHR(hr);
                return hr;
            }
            return -1;
        }

        public override IEnumerable<Mode> GetModes()
        {
            bool needsCleanUp = false;

            if (modes == null || modes.Length == 0)
            {
                // Temp create all these things just to get the modes..
                if (filterGraph == null)
                {
                    filterGraph = new FilterGraph() as IFilterGraph2;
                    capFilter = GetCapFilter();
                    if (capFilter == null)
                    {
                        return new Mode[0];
                    }
                    capGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
                    int hr = capGraph.SetFiltergraph(filterGraph);
                    DsError.ThrowExceptionForHR(hr);

                    needsCleanUp = true;
                }

                modes = GetModesFromDevice().ToArray();
            }

            if (needsCleanUp)
            {
                CleanUp();
            }

            return modes;
        }

        private List<Mode> GetModesFromDevice()
        {
            List<Mode> modes = new List<Mode>();

            if (capGraph == null || capFilter == null)
                return modes;

            int hr;

            IAMVideoControl videoControl = capFilter as IAMVideoControl;

            object objectStreamConfig;
            // Find the stream config interface
            hr = capGraph.FindInterface(PinCategory.Capture, MediaType.Video, capFilter, typeof(IAMStreamConfig).GUID, out objectStreamConfig);
            DsError.ThrowExceptionForHR(hr);

            IAMStreamConfig videoStreamConfig;
            videoStreamConfig = objectStreamConfig as IAMStreamConfig;
            
            if (videoStreamConfig == null)
            {
                return modes;
            }

            int count, size;
            videoStreamConfig.GetNumberOfCapabilities(out count, out size);

            IntPtr capPointer = Marshal.AllocCoTaskMem(size);
            for (int index = 0; index < count; index++)
            {
                AMMediaType ammediaType = null;
                IntPtr ptr = IntPtr.Zero;

                videoStreamConfig.GetStreamCaps(index, out ammediaType, capPointer);

                VideoInfoHeader v = (VideoInfoHeader)Marshal.PtrToStructure(ammediaType.formatPtr, typeof(VideoInfoHeader));

                Mode mode = new Mode();
                mode.FrameWork = FrameWork.DirectShow;
                mode.Index = index;
                mode.Width = v.BmiHeader.Width;
                mode.Height = v.BmiHeader.Height;
                mode.FrameRate = 10000000 / v.AvgTimePerFrame;
                mode.Format = MediaFoundation.MFHelper.GetFormat(ammediaType.subType);

                bool valid = false;
                Guid[] validTypes = new Guid[]
                {
                    MediaSubType.RGB8,
                    MediaSubType.RGB555,
                    MediaSubType.RGB565,
                    MediaSubType.RGB24,
                    MediaSubType.RGB32,
                    MediaSubType.MJPG,
                    MediaSubType.YUY2,
                    MediaSubType.UYVY,
                    MediaSubType.YVYU,

                    // I dont understand these, they seem to often cause memory corruption based on device / available filters? Hard to say!
                    // Update, it's based on render hardware that is available. I think. So we'd need to detect it and only allow certain types..
                    /*MediaSubType.NV12,
                    MediaSubType.I420, */
                };

                if (validTypes.Contains(ammediaType.subType))
                {
                    valid = true;
                }

                if (ammediaType.majorType != MediaType.Video)
                {
                    valid = false;
                }

                if (mode.Width == 0 || mode.Height == 0)
                {
                    valid = false;
                }

                DsUtils.FreeAMMediaType(ammediaType);

                if (valid)
                {
                    modes.Add(mode);
                }
            }

            return modes;
        }

        private bool SetMode(Mode videoMode)
        {
            try
            {
                int hr;

                bool result = false;

                int closestIndex = -1;
                int closestDistance = int.MaxValue;

                if (capGraph != null)
                {
                    Marshal.ReleaseComObject(capGraph);
                    capGraph = null;
                }

                capGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
                hr = capGraph.SetFiltergraph(filterGraph);
                DsError.ThrowExceptionForHR(hr);

                if (videoMode != null)
                {
                    closestIndex = videoMode.Index;
                }

                // Get the modes, these will be cached for later if they're not used right now.
                IEnumerable<Mode> modes = GetModes();

                if (closestIndex < 0)
                {
                    foreach (Mode mode in modes)
                    {
                        int distance = Math.Abs(mode.Width - videoMode.Width) + Math.Abs(mode.Height - videoMode.Height);

                        if (distance <= closestDistance &&
                            mode.FrameRate >= videoMode.FrameRate)
                        {
                            closestDistance = distance;
                            closestIndex = mode.Index;
                        }
                    }
                }

                AMMediaType ammediaType;
                if (closestIndex >= 0)
                {
                    ammediaType = SetCaptureMediaType(closestIndex);
                    if (ammediaType != null)
                    {
                        VideoInfoHeader v = (VideoInfoHeader)Marshal.PtrToStructure(ammediaType.formatPtr, typeof(VideoInfoHeader));
                        AverageTimePerFrame = (int)v.AvgTimePerFrame;

                        result = true;

                        string format = MediaFoundation.MFHelper.GetFormat(ammediaType.subType);
                        double fps = 10000000 / v.AvgTimePerFrame;
                        Tools.Logger.VideoLog.Log(this, DeviceName + " mode ", "Width: " + v.BmiHeader.Width + " Height: " + v.BmiHeader.Height + " FPS: " + fps + " Format: " + format);
                    }
                }
                else
                {
                    ammediaType = GetSourceMediaType();
                    if (ammediaType != null)
                    { 
                        if (ammediaType.subType == MediaSubType.UYVY)
                        {
                            SubType = ammediaType.subType;
                            Tools.Logger.VideoLog.Log(this, DeviceName + " UYVY");
                        }

                        VideoInfoHeader v = (VideoInfoHeader)Marshal.PtrToStructure(ammediaType.formatPtr, typeof(VideoInfoHeader));
                        AverageTimePerFrame = (int)v.AvgTimePerFrame;
                    }

                    Tools.Logger.VideoLog.Log(this, DeviceName + " default video mode");
                }
                DsUtils.FreeAMMediaType(ammediaType);

                Direction = MediaFoundation.MFHelper.GetDirection(SubType);

                return result;
            }
            catch
            {
                return false;
            }
        }


        public override AMMediaType GetSourceMediaType()
        {
            AMMediaType ammediaType;
            
            object objectStreamConfig;
            // Find the stream config interface
            int hr = capGraph.FindInterface(PinCategory.Capture, MediaType.Video, capFilter, typeof(IAMStreamConfig).GUID, out objectStreamConfig);
            DsError.ThrowExceptionForHR(hr);
            
            IAMStreamConfig videoStreamConfig = objectStreamConfig as IAMStreamConfig;
            if (videoStreamConfig != null)
            {
                hr = videoStreamConfig.GetFormat(out ammediaType);
                DsError.ThrowExceptionForHR(hr);
                return ammediaType;
            }
            return null;
        }

        public AMMediaType SetCaptureMediaType(int index)
        {
            AMMediaType ammediaType;

            object objectStreamConfig;
            // Find the stream config interface
            int hr = capGraph.FindInterface(PinCategory.Capture, MediaType.Video, capFilter, typeof(IAMStreamConfig).GUID, out objectStreamConfig);
            DsError.ThrowExceptionForHR(hr);

            IAMStreamConfig videoStreamConfig = objectStreamConfig as IAMStreamConfig;
            if (videoStreamConfig != null)
            {
                int count = 0, size = 0;
                SubType = MediaSubType.RGB24;
                videoStreamConfig.GetNumberOfCapabilities(out count, out size);

                IntPtr capPointer = Marshal.AllocCoTaskMem(size);

                videoStreamConfig.GetStreamCaps(index, out ammediaType, capPointer);

                hr = videoStreamConfig.SetFormat(ammediaType);
                DsError.ThrowExceptionForHR(hr);

                Marshal.FreeCoTaskMem(capPointer);

                return ammediaType;
            }
            return null;
        }
    }

    public class MissingVideoDeviceException : Exception
    {
        public MissingVideoDeviceException(string message)
            :base(message)
        {
        }
    }

    public class InsufficientResourcesException : Exception
    {
        public InsufficientResourcesException()
            : base()
        {
        }
    }
}
