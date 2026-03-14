using DirectShowLib;
using ImageServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WindowsMediaPlatform.MediaFoundation;

namespace WindowsMediaPlatform.DirectShow
{
    public class DirectShowFramework : VideoFrameWork
    {
        public FrameWork FrameWork { get { return FrameWork.DirectShow; } }
        public bool NeedsInstall { get => false; }

        public FrameSource CreateFrameSource(VideoConfig videoConfig)
        {
            if (videoConfig.RecordVideoForReplays || videoConfig.HasPhotoBooth)
            {
                return new DirectShowCaptureFrameSource(videoConfig);
            }
            else
            {
                return new DirectShowDeviceFrameSource(videoConfig);
            }
        }
        public FrameSource CreateFrameSource(string filename)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetAudioSources()
        {
            yield break;
        }

        public IEnumerable<string> GetFileExtensions()
        {
            yield return ".wmv";
        }

        public IEnumerable<VideoConfig> GetVideoConfigs()
        {
            List<VideoConfig> configs = new List<VideoConfig>();

            foreach (DsDevice ds in DirectShowHelper.VideoCaptureDevices)
            {
                VideoConfig videoConfig = new VideoConfig() { DeviceName = ds.Name, DirectShowPath = ds.DevicePath, FrameWork = FrameWork.DirectShow };
                ds.Dispose();
                configs.Add(videoConfig);
            }

            return configs;
        }

        public Mode PickMode(IEnumerable<Mode> modes)
        {
            return modes.FirstOrDefault();
        }

        public void Install()
        {
            throw new NotImplementedException();
        }
    }
}
