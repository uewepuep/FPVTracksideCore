using System;
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

                case FrameWork.ffmpeg:
                    return "ff";

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

                case FrameWork.ffmpeg:
                    return "ffmpeg         ";

                default:
                    return frameWork.ToString();
            }
        }

        public static IEnumerable<VideoConfig> CombineVideoSources(this IEnumerable<VideoConfig> sources)
        {
            List<VideoConfig> configs = new List<VideoConfig>();
            foreach (VideoConfig videoConfig in sources)
            {
                VideoConfig fromAnotherFramework = GetMatch(configs.Where(r => r.DeviceName == videoConfig.DeviceName), videoConfig.MediaFoundationPath, videoConfig.DirectShowPath);
                if (fromAnotherFramework != null)
                {
                    if (fromAnotherFramework.DirectShowPath == null)
                        fromAnotherFramework.DirectShowPath = videoConfig.DirectShowPath;

                    if (fromAnotherFramework.MediaFoundationPath == null)
                        fromAnotherFramework.MediaFoundationPath = videoConfig.MediaFoundationPath;

                    if (fromAnotherFramework.ffmpegId == null)
                        fromAnotherFramework.ffmpegId = videoConfig.ffmpegId;
                }
                else
                {
                    configs.Add(videoConfig);
                }
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

        private static VideoConfig GetMatch(IEnumerable<VideoConfig> videoConfigs, params string[] paths)
        {
            if (paths.Any())
            {
                Regex regex = new Regex("(#[A-z0-9_&#]*)");

                foreach (string path in paths)
                {
                    if (string.IsNullOrEmpty(path))
                        continue;

                    Match match = regex.Match(path);
                    if (match.Success)
                    {
                        string common = match.Groups[1].Value;
                        return videoConfigs.Where(v => v.PathContains(common)).FirstOrDefault();
                    }
                }
            }
            return null;
        }
    }
}
