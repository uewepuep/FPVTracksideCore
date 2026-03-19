using ImageServer;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.Direct3D9;
using WindowsMediaPlatform.DirectShow;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationFramework : VideoFrameWork
    {
        public FrameWork FrameWork { get { return FrameWork.MediaFoundation; } }

        public GraphicsDevice GraphicsDevice { get; set; }

        public IEnumerable<string> GetFileExtensions()
        {
            yield return ".mp4";
            yield return ".wmv";
        }

        public MediaFoundationFramework(GraphicsDevice graphicsDevice) 
        {
            GraphicsDevice = graphicsDevice;
        }

        public FrameSource CreateFrameSource(VideoConfig videoConfig)
        {
            bool isDSOnly = string.IsNullOrEmpty(videoConfig.MediaFoundationPath) && !string.IsNullOrEmpty(videoConfig.DirectShowPath);

            if (!string.IsNullOrEmpty(videoConfig.FilePath))
            {
                return new MediaFoundationFileFrameSource(videoConfig);
            }
            else if (isDSOnly)
            {
                return new MediaFoundationDSCaptureFrameSource(videoConfig);
            }
            else if (videoConfig.RecordVideoForReplays || videoConfig.HasPhotoBooth)
            {
                return new MediaFoundationCaptureFrameSource(videoConfig, GraphicsDevice);
            }
            else
            {
                return new MediaFoundationDeviceFrameSource(videoConfig);
            }
        }

        public bool NeedsInstall { get => false; }

        public FrameSource CreateFrameSource(string filename)
        {
            VideoConfig videoConfig = new VideoConfig()
            {
                FilePath = filename
            };

            return new MediaFoundationFileFrameSource(videoConfig);
        }

        public IEnumerable<VideoConfig> GetVideoConfigs()
        {
            List<VideoConfig> configs = new List<VideoConfig>();
            HashSet<string> mfNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (MFDevice mf in MFHelper.VideoCaptureDevices)
            {
                Tools.Logger.VideoLog.Log(this, "MF device found", mf.Name + " Path: " + mf.Path);
                VideoConfig videoConfig = new VideoConfig() { DeviceName = mf.Name, MediaFoundationPath = mf.Path, FrameWork = FrameWork.MediaFoundation };
                configs.Add(videoConfig);
                mfNames.Add(mf.Name);
                mf.Dispose();
            }

            // DS-only virtual cameras (e.g. OBS Virtual Camera) not visible to MFEnumDeviceSources.
            // Captured via DirectShow, recorded via IMFSinkWriter + NVENC.
            foreach (DirectShowLib.DsDevice ds in DirectShowHelper.VideoCaptureDevices)
            {
                if (!mfNames.Contains(ds.Name))
                {
                    Tools.Logger.VideoLog.Log(this, "DS-only device added to MF", ds.Name + " Path: " + ds.DevicePath);
                    VideoConfig videoConfig = new VideoConfig() { DeviceName = ds.Name, DirectShowPath = ds.DevicePath, FrameWork = FrameWork.MediaFoundation };
                    configs.Add(videoConfig);
                }
                ds.Dispose();
            }

            return configs;
        }

        public IEnumerable<string> GetAudioSources()
        {
            List<string> configs = new List<string>();
            foreach (MFDevice mf in MFHelper.AudioCaptureDevices)
            {
                configs.Add(mf.Name);
                mf.Dispose();
            }
            return configs;
        }

        public Mode PickMode(IEnumerable<Mode> modes)
        {
            return modes.OrderBy(r => FormatOrder(r)).FirstOrDefault();
        }

        public static int FormatOrder(Mode mode)
        {
            Guid subTypeId = MFHelper.GetSubType(mode.Format);

            int index = 0;

            foreach (Guid st in MediaFoundationCaptureFrameSource.RecordingSubTypes())
            {
                if (st == subTypeId)
                {
                    return index;
                }
                index++;
            }

            foreach (Guid st in MediaFoundationDeviceFrameSource.SupportedSubTypes())
            {
                if (st == subTypeId)
                {
                    return index;
                }
                index++;
            }

            return 1000;
        }

        public void Install()
        {
            throw new NotImplementedException();
        }
    }
}
