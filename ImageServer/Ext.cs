using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tools;

namespace ImageServer
{
    public static class Ext
    {
        public static string ToHumanString(this Splits split)
        {
            switch (split)
            {
                default:
                case Splits.SingleChannel:
                    return split.ToString().CamelCaseToHuman();
                case Splits.TwoByTwo:
                    return "2 x 2";
                case Splits.ThreeByTwo:
                    return "3 x 2";
                case Splits.ThreeByThree:
                    return "3 x 3";
                case Splits.ThreeByFour:
                    return "3 x 4";
                case Splits.FourByFour:
                    return "4 x 4";
                case Splits.FourByThree:
                    return "4 x 3";
                case Splits.FourByTwo:
                    return "4 x 2";
            }
        }

        public static string ToStringShort(this FrameWork frameWork)
        {
            switch (frameWork)
            {
                case FrameWork.MediaFoundation:
                    return "MF";

                case FrameWork.DirectShow:
                    return "DS";

                case FrameWork.FFmpeg:
                    return "FF";

                default:
                    return frameWork.ToString().Substring(0, 2);
            }
        }

        public static string ToStringLong(this FrameWork frameWork)
        {
            switch (frameWork)
            {
                case FrameWork.MediaFoundation:
                    return "MediaFoundation";

                case FrameWork.DirectShow:
                    return "DirectShow     ";

                case FrameWork.FFmpeg:
                    return "FFmpeg         ";

                default:
                    return frameWork.ToString();
            }
        }

        public static IEnumerable<VideoConfig> CombineVideoSources(this IEnumerable<VideoConfig> sources)
        {
            List<VideoConfig> configs = new List<VideoConfig>();
            List<VideoConfig> toRemove = new List<VideoConfig>();

            foreach (VideoConfig videoConfig in sources)
            {
                if (toRemove.Contains(videoConfig))
                    continue;

                IEnumerable<VideoConfig> notCurrent = sources.Where(r => r != videoConfig);
                IEnumerable<VideoConfig> fromAnotherFramework = GetMatch(videoConfig, notCurrent);
                if (fromAnotherFramework.Any())
                {
                    foreach (VideoConfig another in fromAnotherFramework)
                    {
                        if (videoConfig.DirectShowPath == null)
                            videoConfig.DirectShowPath = another.DirectShowPath;

                        if (videoConfig.MediaFoundationPath == null)
                            videoConfig.MediaFoundationPath = another.MediaFoundationPath;

                        if (videoConfig.ffmpegId == null)
                            videoConfig.ffmpegId = another.ffmpegId;
                    }

                    toRemove.AddRange(fromAnotherFramework);
                }
                configs.Add(videoConfig);
            }


            // Set any usbports
            foreach (VideoConfig vc in configs)
            {
                if (configs.Where(other => other.DeviceName == vc.DeviceName).Count() > 1)
                {
                    vc.AnyUSBPort = false;
                }
                else
                {
                    vc.AnyUSBPort = true;
                }
            }

            return configs;
        }

        private static IEnumerable<VideoConfig> GetMatch(VideoConfig matchThis, IEnumerable<VideoConfig> videoConfigs)
        {
            foreach (VideoConfig videoConfig in videoConfigs)
            {
                if (matchThis.Matches(videoConfig))
                    yield return videoConfig;
            }
        }
    }
}
