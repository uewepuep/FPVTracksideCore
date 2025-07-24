using ImageServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            // Use local ffmpeg binaries from ./ffmpeg directory on Mac
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                // Get the current directory and construct path to ffmpeg binaries
                string currentDir = Directory.GetCurrentDirectory();
                
                // Try multiple possible locations for ffmpeg binaries
                string[] possiblePaths = {
                    Path.Combine(currentDir, "ffmpeg"), // If running from FPVMacsideCore directory
                    Path.Combine(currentDir, "FPVMacsideCore", "ffmpeg"), // If running from parent directory
                    Path.Combine(currentDir, "bin", "Debug", "net6.0", "ffmpeg"), // Output directory
                    Path.Combine(currentDir, "bin", "Release", "net6.0", "ffmpeg") // Release output directory
                };
                
                string ffmpegDir = null;
                foreach (string path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        ffmpegDir = path;
                        Console.WriteLine($"Found ffmpeg directory: {ffmpegDir}");
                        break;
                    }
                }
                
                // Detect Mac architecture and select appropriate ffmpeg binary
                var processArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
                string ffmpegPath = null;
                
                if (ffmpegDir != null)
                {
                    if (processArch == System.Runtime.InteropServices.Architecture.Arm64)
                    {
                        ffmpegPath = Path.Combine(ffmpegDir, "ffmpeg-arm");
                    }
                    else
                    {
                        ffmpegPath = Path.Combine(ffmpegDir, "ffmpeg-intel");
                    }
                    
                    Console.WriteLine($"Looking for ffmpeg binary at: {ffmpegPath}");
                    
                    // Check if the local ffmpeg binary exists
                    if (File.Exists(ffmpegPath))
                    {
                        execName = ffmpegPath;
                        Console.WriteLine($"Using local ffmpeg binary: {ffmpegPath} for architecture: {processArch}");
                    }
                    else
                    {
                        Console.WriteLine($"Local ffmpeg binary not found at: {ffmpegPath}");
                        ffmpegPath = null; // Reset to null so we fall back
                    }
                }
                
                // Fallback to Homebrew ffmpeg if local binary not found
                if (ffmpegPath == null)
                {
                    execName = "ffmpeg"; // fallback to PATH
                    Console.WriteLine("Local and Homebrew ffmpeg not found, using system PATH ffmpeg");
                }
            }
            else
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
            }

            IEnumerable<string> devices = GetFfmpegText("-devices");
            dshow = devices?.Any(l => l != null && l.Contains("dshow")) ?? false;
            avfoundation = devices?.Any(l => l != null && l.Contains("avfoundation")) ?? false; // Default to true on Mac

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
                    // Remove trailing VID/PID if present (for both Windows and Mac cameras)
                    string cleanedName = System.Text.RegularExpressions.Regex.Replace(name, @"\s*VID:[0-9A-Fa-f]+\s*PID:[0-9A-Fa-f]+", "").Trim();
                    yield return new VideoConfig { FrameWork = FrameWork.ffmpeg, DeviceName = cleanedName, ffmpegId = cleanedName };
                }
            }

            if (avfoundation)
            {
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
                        Regex reg = new Regex("\\[AVFoundation[^\\]]*\\] \\[(\\d+)\\] (.+)");

                        Match match = reg.Match(deviceLine);
                        if (match.Success)
                        {
                            string deviceIndex = match.Groups[1].Value;
                            string rawName = match.Groups[2].Value;
                            // Remove trailing VID/PID if present (for mac cameras only)
                            string cleanedName = System.Text.RegularExpressions.Regex.Replace(rawName, @"\s*VID:[0-9A-Fa-f]+\s*PID:[0-9A-Fa-f]+", "").Trim();
                            // For AVFoundation, use cleaned device name for FFmpeg (without VID:PID)
                            // On Mac, cameras are upside down by default, but we want UI to show "None"
                            yield return new VideoConfig { 
                                FrameWork = FrameWork.ffmpeg, 
                                DeviceName = cleanedName, 
                                ffmpegId = cleanedName,
                                FlipMirrored = FlipMirroreds.None  // UI shows "None" but flip logic handles Mac cameras
                            };
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
            if (!modes.Any())
                return null;

            // 1st priority: 640x480 @ 30fps
            var preferred = modes.FirstOrDefault(m => 
                m.Width == 640 && m.Height == 480 && m.FrameRate >= 30);
            if (preferred != null)
                return preferred;

            // 2nd priority: lowest resolution above 30fps
            var above30fps = modes
                .Where(m => m.FrameRate >= 30)
                .OrderBy(m => m.Width * m.Height)
                .ThenBy(m => m.FrameRate)
                .FirstOrDefault();
            if (above30fps != null)
                return above30fps;

            // 3rd priority: best available (highest framerate, then lowest resolution)
            return modes
                .OrderByDescending(m => m.FrameRate)
                .ThenBy(m => m.Width * m.Height)
                .First();
        }

        public FrameSource CreateFrameSource(string filename)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetAudioSources()
        {
            yield break;
        }
    }
}