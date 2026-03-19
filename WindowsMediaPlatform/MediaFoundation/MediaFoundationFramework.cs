using ImageServer;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.Direct3D9;
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
            if (!string.IsNullOrEmpty(videoConfig.FilePath))
            {
                return new MediaFoundationFileFrameSource(videoConfig);
            }
            else if (videoConfig.RecordVideoForReplays || videoConfig.HasPhotoBooth)
            {
                return new MediaFoundationCaptureFrameSourceHW(videoConfig, GraphicsDevice);
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
            foreach (MFDevice mf in MFHelper.VideoCaptureDevices)
            {
                VideoConfig videoConfig = new VideoConfig() { DeviceName = mf.Name, MediaFoundationPath = mf.Path, FrameWork = FrameWork.MediaFoundation };
                configs.Add(videoConfig);

                mf.Dispose();
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
