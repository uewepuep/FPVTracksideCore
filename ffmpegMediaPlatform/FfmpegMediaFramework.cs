using ImageServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FfmpegMediaPlatform
{
    public class FfmpegMediaFramework : VideoFrameWork
    {
        public FrameWork FrameWork
        {
            get
            {
                return FrameWork.ffmpeg;
            }
        }

        private string execName;

        private bool avfoundation;
        private bool dshow;

        public FfmpegMediaFramework()
        {
            execName = "ffmpeg";

            if (!File.Exists(execName))
            {
                string exe = execName + ".exe";

                if(File.Exists(exe))
                {
                    execName = exe;
                }
            }

            IEnumerable<string> devices = GetFfmpegText("-devices");
            dshow = devices.Any(l => l.Contains("dshow"));
            avfoundation = devices.Any(l => l.Contains("avfoundation"));

            GetVideoConfigs();

        }

        public ProcessStartInfo GetProcessStartInfo(string args)
        {
            return new ProcessStartInfo()
            {
                 Arguments = args,
                FileName = execName,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        public IEnumerable<string> GetFfmpegText(string args, Func<string, bool> predicate = null)
        {
            try
            {
                List<string> output = new List<string>();

                ProcessStartInfo processStartInfo = GetProcessStartInfo(args);

                using (Process process = new Process())
                {
                    process.StartInfo = processStartInfo;
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            output.Add(e.Data);
                        }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            output.Add(e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    process.CancelOutputRead();
                }

                if (predicate != null)
                {
                    return output.Where(x => predicate(x));
                }
                return output;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return new string[] { };
            }            
        }

        public FrameSource CreateFrameSource(VideoConfig vc)
        {
            if (dshow)
                return new FfmpegDshowFrameSource(this, vc);
            else
                return new FfmpegAvFoundationFrameSource(this, vc);
        }

        public IEnumerable<VideoConfig> GetVideoConfigs()
        {
            if (dshow)
            {
                IEnumerable<string> deviceList = GetFfmpegText("-list_devices true -f dshow -i dummy", l => l.Contains("[dshow @") && l.Contains("(video)"));

                foreach (string deviceLine in deviceList)
                {
                    string[] splits = deviceLine.Split("\"");
                    if (splits.Length != 3)
                    {
                        continue;
                    }
                    string name = splits[1];

                    yield return new VideoConfig { FrameWork = FrameWork.ffmpeg, DeviceName = name, ffmpegId = name };
                }
            }

            if (avfoundation)
            {

                //"[AVFoundation indev @ 0x7fec24704c00] AVFoundation video devices:"
                //"	[12]	"[AVFoundation indev @ 0x7fb47c004a00] [0] Razer Kiyo Pro"	
                IEnumerable<string> deviceList = GetFfmpegText("-list_devices true -f avfoundation -i dummy", l => l.Contains("AVFoundation"));

                bool inVideo = false;

                foreach (string deviceLine in deviceList)
                {
                    if (deviceLine.Contains("video devices:"))
                    {
                        inVideo = true;
                        continue;
                    }

                    if (deviceLine.Contains("audio devices:"))
                    {
                        inVideo = false;
                        continue;
                    }

                    if (inVideo)
                    {
                        Regex reg = new Regex("\\] \\[([A-z0-9]*)\\] ([A-z0-9 ]*)");

                        Match match = reg.Match(deviceLine);
                        if (match.Success)
                        {

                            yield return new VideoConfig { FrameWork = FrameWork.ffmpeg, DeviceName = match.Groups[2].Value, ffmpegId = match.Groups[1].Value };
                        }
                    }
                }
            }

        }

        public string GetValue(string source, string name)
        {
            Regex reg = new Regex(name + "=([A-z0-9]*)");

            Match match = reg.Match(source);
            if (match.Success && match.Groups.Count > 1) 
            { 
                return match.Groups[1].Value;
            }

            return "";
        }

        public Mode PickMode(IEnumerable<Mode> modes)
        {
            throw new NotImplementedException();
        }

        public FrameSource CreateFrameSource(string filename)
        {
            throw new NotImplementedException();
        }
    }
}