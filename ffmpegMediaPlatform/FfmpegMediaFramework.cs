using ImageServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Tools;

namespace FfmpegMediaPlatform
{
    public class FfmpegMediaFramework : VideoFrameWork
    {
        public FrameWork FrameWork
        {
            get
            {
                return FrameWork.FFmpeg;
            }
        }

        private string execName;

        public string ExecName => execName;

        private bool avfoundation;
        private bool dshow;

        public FfmpegMediaFramework()
        {
            // Use local ffmpeg binaries from ./ffmpeg directory on Mac
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                avfoundation = true;

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
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                dshow = true;

                // On Windows, try to find ffmpeg.exe in the ffmpeg subdirectory first
                string localFfmpegPath = Path.Combine("ffmpeg", "ffmpeg.exe");
                
                if (File.Exists(localFfmpegPath))
                {
                    execName = localFfmpegPath;
                    Console.WriteLine($"Using local ffmpeg binary: {localFfmpegPath}");
                }
                else
                {
                    // Stupid github wont allow > 100mb file, so it's zipped.
                    string localZipFile = "ffmpeg.zip";
                    try
                    {
                        if (File.Exists(localZipFile))
                        {
                            Console.WriteLine($"Decompressing {localZipFile}");
                            System.IO.Compression.ZipFile.ExtractToDirectory(localZipFile, ".");
                            File.Delete(localZipFile);
                        }
                    }
                    catch (Exception ex) 
                    {
                        Console.WriteLine($"Failed to Decompress {localZipFile} {ex.Message}");
                    }


                    // Fallback to looking for ffmpeg in current directory or PATH
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
            }
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
            // Check if this is a video file playback (has FilePath) or camera capture
            if (!string.IsNullOrEmpty(vc.FilePath))
            {
                // On macOS, try to use native dylibs for video file replay first, fallback to external process if needed
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    try
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"PLAYBACK PATH: Native ffmpeg lib for Mac video file replay → {System.IO.Path.GetFileName(vc.FilePath)}");
                        return new FfmpegLibVideoFileFrameSource(vc);
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, "Native library failed, falling back to external ffmpeg process", ex);
                        Tools.Logger.VideoLog.LogDebugCall(this, $"PLAYBACK PATH: External ffmpeg process for Mac video file replay → {System.IO.Path.GetFileName(vc.FilePath)}");
                        return new FfmpegVideoFileFrameSource(this, vc);
                    }
                }
                else
                {
                    // On other platforms, prefer in-process libav for replay; fallback to external binary if init fails
                    try
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"PLAYBACK PATH: In-process ffmpeg lib for video file replay → {System.IO.Path.GetFileName(vc.FilePath)}");
                        return new FfmpegLibVideoFileFrameSource(vc);
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, "PLAYBACK PATH FALLBACK: External ffmpeg process due to lib error", ex);
                        return new FfmpegVideoFileFrameSource(this, vc);
                    }
                }
            }
            else
            {
                // Live camera capture via ffmpeg process with HLS composite
                Tools.Logger.VideoLog.LogDebugCall(this, $"PLAYBACK PATH: Live capture via ffmpeg (HLS composite) → {vc.DeviceName}");
                return new FfmpegHlsCompositeFrameSource(this, vc);
            }
        }

        public IEnumerable<VideoConfig> GetVideoConfigs()
        {
            if (dshow)
            {
                string listDevicesCommand = "-list_devices true -f dshow -i dummy";
                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG COMMAND (list cameras): ffmpeg {listDevicesCommand}");

                IEnumerable<string> responseText = GetFfmpegText(listDevicesCommand);
               
                string[] deviceList = responseText.Where(l => l.Contains("[dshow @") && l.Contains("(video)")).ToArray();
                string[] alternativeNames = responseText.Where(l => l.Contains("[dshow @") && l.Contains("Alternative name")).ToArray();
                for (int i = 0; i < deviceList.Length && i < alternativeNames.Length; i++)
                {
                    string deviceLine = deviceList[i];
                    string alternativeName = alternativeNames[i];

                    Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG OUTPUT: {deviceLine}");

                    string[] splits = deviceLine.Split("\"");
                    if (splits.Length != 3)
                    {
                        continue;
                    }

                    string dshowPath = "";
                    if (!string.IsNullOrEmpty(alternativeName))
                    {
                        Match m = System.Text.RegularExpressions.Regex.Match(alternativeName, "\"(.*)\"");
                        if (m.Success)
                        {
                            dshowPath = m.Groups[1].Value;
                        }
                    }
                    string name = splits[1];
                    yield return new VideoConfig { FrameWork = FrameWork.FFmpeg, DeviceName = name, ffmpegId = dshowPath };
                }
            }

            if (avfoundation)
            {
                string listDevicesCommand = "-list_devices true -f avfoundation -i dummy";
                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG COMMAND (list cameras): ffmpeg {listDevicesCommand}");
                
                IEnumerable<string> deviceList = GetFfmpegText(listDevicesCommand, l => l.Contains("AVFoundation"));

                bool inVideo = false;

                foreach (string deviceLine in deviceList)
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG OUTPUT: {deviceLine}");
                    
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
                            // IMPORTANT: Don't trim the name - FFmpeg expects the exact name including any trailing/leading spaces
                            string cleanedName = System.Text.RegularExpressions.Regex.Replace(rawName, @"\s*VID:[0-9A-Fa-f]+\s*PID:[0-9A-Fa-f]+", "");
                            Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG ✓ FOUND CAMERA: '{cleanedName}' (length: {cleanedName.Length})");
                            // For AVFoundation, use cleaned device name for FFmpeg (without VID:PID)
                            // On Mac, cameras are upside down by default, but we want UI to show "None"
                            yield return new VideoConfig {
                                FrameWork = FrameWork.FFmpeg,
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
        public Mode DetectOptimalMode(IEnumerable<Mode> availableModes)
        {
            if (availableModes.Any())
            {
                // 1st priority: 640x480 @ 30fps
                var preferred = availableModes.FirstOrDefault(m =>
                    m.Width == 640 && m.Height == 480 && m.FrameRate >= 30);
                if (preferred != null)
                {
                    Logger.VideoLog.LogDebugCall(this, $"✓ SELECTED (1st priority - preferred): {preferred.Width}x{preferred.Height}@{preferred.FrameRate}fps");
                    return preferred;
                }
                else
                {
                    Logger.VideoLog.LogDebugCall(this, "✗ 640x480@30fps not available, trying next priority");
                }

                // 2nd priority: lowest resolution above 30fps
                var above30fps = availableModes
                    .Where(m => m.FrameRate >= 30)
                    .OrderBy(m => m.Width * m.Height)
                    .ThenBy(m => m.FrameRate)
                    .FirstOrDefault();
                if (above30fps != null)
                {
                    Logger.VideoLog.LogDebugCall(this, $"✓ SELECTED (2nd priority - lowest above 30fps): {above30fps.Width}x{above30fps.Height}@{above30fps.FrameRate}fps");
                    return above30fps;
                }
                else
                {
                    Logger.VideoLog.LogDebugCall(this, "✗ No modes above 30fps available, trying best available");
                }

                // 3rd priority: best available resolution (highest framerate, then lowest resolution)
                var bestMode = availableModes
                    .OrderByDescending(m => m.FrameRate)
                    .ThenBy(m => m.Width * m.Height)
                    .FirstOrDefault();
                if (bestMode != null)
                {
                    Logger.VideoLog.LogDebugCall(this, $"✓ SELECTED (3rd priority - best available): {bestMode.Width}x{bestMode.Height}@{bestMode.FrameRate}fps");
                    return bestMode;
                }
            }
            else
            {
                Logger.VideoLog.LogDebugCall(this, "WARNING: No modes detected from camera");
            }

            return null;
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