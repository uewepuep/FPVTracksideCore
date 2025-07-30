using UI.Nodes;
using ImageServer;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using System.Threading;
using System.IO;
using Microsoft.Xna.Framework.Media;
using Composition;
using System.Text.RegularExpressions;
using UI;
using static UI.Video.VideoManager;

namespace UI.Video
{
    public class VideoManager : IDisposable
    {
        public List<VideoConfig> VideoConfigs { get; private set; }

        private List<FrameSource> frameSources;
        public IEnumerable<FrameSource> FrameSources { get { return frameSources; } }

        public int ConnectedCount
        {
            get
            {
                lock (frameSources)
                {
                    return frameSources.Where(d => d.Connected).Count();
                }
            }
        }

        public bool Connected
        {
            get
            {
                lock (frameSources)
                {
                    return frameSources.All(d => d.Connected);
                }
            }
        }

        public bool MaintainConnections { get; set; }

        private Thread videoDeviceManagerThread;
        private CancellationTokenSource cancellationTokenSource;
        private bool runWorker;
        private List<Action> todo;

        private List<ICaptureFrameSource> recording;
        private Race race;

        private List<FrameSource> needsInitialize;
        private List<IDisposable> needsDispose;

        public bool RunningDevices { get { return runWorker; } }

        public bool AutoPause { get; set; }
        public bool NeedsInit
        {
            get
            {
                return needsInitialize.Count > 0;
            }
        }

        public int DeviceCount { get { return VideoConfigs.Count; } }

        public delegate void FrameSourceDelegate(FrameSource frameSource);
        public delegate void FrameSourcesDelegate(IEnumerable<FrameSource> frameSources);

        public event FrameSourceDelegate OnStart;

        private AutoResetEvent mutex;

        public DirectoryInfo EventDirectory { get; private set; }

        public Profile Profile { get; private set; }

        public bool Finalising
        {
            get
            {
                lock (frameSources)
                {
                    return frameSources.OfType<ICaptureFrameSource>().Any(r => r.Finalising);
                }
            }
        }

        public event Action OnFinishedFinalizing;

        public VideoManager(string eventDirectory, Profile profile)
        {
            Profile = profile;
            EventDirectory = new DirectoryInfo(eventDirectory);

            todo = new List<Action>();
            mutex = new AutoResetEvent(false);
            Logger.VideoLog.LogCall(this);

            recording = new List<ICaptureFrameSource>();
            needsInitialize = new List<FrameSource>();
            needsDispose = new List<IDisposable>();

            frameSources = new List<FrameSource>();
            VideoConfigs = new List<VideoConfig>();
            AutoPause = false;
        }

        private VideoFrameWork GetFramework(FrameWork frameWork)
        {
            return VideoFrameWorks.Available.FirstOrDefault(f => f.FrameWork == frameWork);
        }

        public void LoadCreateDevices(FrameSourcesDelegate frameSources)
        {
            LoadDevices();
            CreateFrameSource(VideoConfigs, frameSources);
        }


        public void LoadDevices()
        {
            Clear();

            MaintainConnections = true;

            VideoConfigs.Clear();
            VideoConfig[] savedConfigs = VideoConfig.Read(Profile);
            
            if (savedConfigs.Length == 0)
            {
                Logger.VideoLog.LogCall(this, "No saved video configuration found - leaving video config empty for user to manually add cameras");
                // Don't auto-populate with available cameras - let the user add them manually
                // Save the empty configuration so we don't keep showing available cameras on restart
                VideoManager.WriteDeviceConfig(Profile, VideoConfigs); // VideoConfigs is empty at this point
            }
            else
            {
                Logger.VideoLog.LogCall(this, $"Found {savedConfigs.Length} saved video configuration(s) - validating and updating with current camera capabilities");
                
                // Get currently available cameras
                var availableConfigs = GetAvailableVideoSources();
                Logger.VideoLog.LogCall(this, $"Currently detected {availableConfigs.Count()} available camera(s)");
                
                bool configurationUpdated = false;
                
                // Update existing saved configurations with optimal modes
                foreach (var savedConfig in savedConfigs)
                {
                    Logger.VideoLog.LogCall(this, $"Validating saved camera: '{savedConfig.DeviceName}' (Current: {savedConfig.VideoMode.Width}x{savedConfig.VideoMode.Height}@{savedConfig.VideoMode.FrameRate}fps)");
                    
                    // Find matching available camera
                    var availableConfig = availableConfigs.FirstOrDefault(ac => ac.Equals(savedConfig));
                    if (availableConfig != null)
                    {
                        Logger.VideoLog.LogCall(this, $"Camera '{savedConfig.DeviceName}' is still available - detecting optimal mode...");
                        
                        // Use the framework from available config (may be more current)
                        savedConfig.FrameWork = availableConfig.FrameWork;
                        
                        var optimalMode = DetectOptimalMode(savedConfig);
                        if (optimalMode != null)
                        {
                            bool modeChanged = savedConfig.VideoMode.Width != optimalMode.Width || 
                                             savedConfig.VideoMode.Height != optimalMode.Height || 
                                             savedConfig.VideoMode.FrameRate != optimalMode.FrameRate;
                            
                            if (modeChanged)
                            {
                                Logger.VideoLog.LogCall(this, $"Updating saved mode from {savedConfig.VideoMode.Width}x{savedConfig.VideoMode.Height}@{savedConfig.VideoMode.FrameRate}fps to {optimalMode.Width}x{optimalMode.Height}@{optimalMode.FrameRate}fps");
                                savedConfig.VideoMode = optimalMode;
                                configurationUpdated = true;
                            }
                            else
                            {
                                Logger.VideoLog.LogCall(this, "Current saved mode is already optimal - no changes needed");
                            }
                        }
                        else
                        {
                            Logger.VideoLog.LogCall(this, $"⚠ Could not detect optimal mode for '{savedConfig.DeviceName}' - keeping existing configuration");
                        }
                    }
                    else
                    {
                        Logger.VideoLog.LogCall(this, $"⚠ Saved camera '{savedConfig.DeviceName}' is no longer available - keeping configuration anyway");
                    }
                }
                
                VideoConfigs.AddRange(savedConfigs);
                
                // Save updated configuration if any changes were made
                if (configurationUpdated)
                {
                    Logger.VideoLog.LogCall(this, "Camera configurations were updated - saving to disk");
                    VideoManager.WriteDeviceConfig(Profile, VideoConfigs);
                }
                
                Logger.VideoLog.LogCall(this, "=== CONFIGURATION VALIDATION COMPLETE ===");
                Logger.VideoLog.LogCall(this, "Final camera configurations:");
                foreach (var config in VideoConfigs)
                {
                    Logger.VideoLog.LogCall(this, $"  📹 {config.DeviceName}: {config.VideoMode.Width}x{config.VideoMode.Height}@{config.VideoMode.FrameRate}fps ({config.FrameWork})");
                }
            }

            StartThread();
        }

        public void StartThread()
        {
            if (videoDeviceManagerThread == null)
            {
                runWorker = true;
                cancellationTokenSource = new CancellationTokenSource();
                videoDeviceManagerThread = new Thread(() => WorkerThread(cancellationTokenSource.Token));
                videoDeviceManagerThread.Name = "Video Device Manager";
                videoDeviceManagerThread.Start();
            }
        }

        public void WriteCurrentDeviceConfig()
        {
            WriteDeviceConfig(Profile, VideoConfigs);
        }

        public static void WriteDeviceConfig(Profile profile, IEnumerable<VideoConfig> vcs)
        {
            VideoConfig.Write(profile,vcs.ToArray());
        }

        public void Dispose()
        {
            StopDevices();
        }

        private void DoOnWorkerThread(Action a)
        {
            lock (todo)
            {
                todo.Add(a);
            }
            mutex.Set();
        }

        private void DisposeOnWorkerThread(IDisposable disposable)
        {
            lock (needsDispose)
            {
                needsDispose.Add(disposable);
            }
            mutex.Set();
        }

        public void StopDevices()
        {
            Clear();

            runWorker = false;
            mutex.Set();

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }

            if (videoDeviceManagerThread != null)
            {
                if (!videoDeviceManagerThread.Join(5000))
                {
                    // Thread didn't exit within timeout - this is unusual but we'll handle it gracefully
                }
                videoDeviceManagerThread = null;
            }

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
        }

        public void Clear()
        {
            Logger.VideoLog.LogCall(this);
            mutex.Set();
            lock (frameSources)
            {
                foreach (var source in frameSources)
                {
                    DisposeOnWorkerThread(source);
                }
                frameSources.Clear();
            }
            mutex.Set();
        }

        public Mode DetectOptimalMode(VideoConfig config)
        {
            Logger.VideoLog.LogCall(this, $"DetectOptimalMode() called for camera: '{config.DeviceName}' (Framework: {config.FrameWork})");
            
            try
            {
                // Find the appropriate framework and query modes directly
                foreach (VideoFrameWork frameWork in VideoFrameWorks.Available)
                {
                    if (config.FrameWork == frameWork.FrameWork)
                    {
                        Logger.VideoLog.LogCall(this, $"Using framework {frameWork.FrameWork} to detect modes");
                        
                        // Create a temporary instance just to get the modes
                        using (FrameSource tempSource = frameWork.CreateFrameSource(config))
                        {
                            if (tempSource is IHasModes hasModes)
                            {
                                Logger.VideoLog.LogCall(this, "Querying available modes from camera...");
                                var availableModes = hasModes.GetModes().ToList();
                                
                                Logger.VideoLog.LogCall(this, $"Camera returned {availableModes.Count} available modes");
                                
                                if (availableModes.Any())
                                {
                                    // 1st priority: 640x480 @ 30fps
                                    var preferred = availableModes.FirstOrDefault(m => 
                                        m.Width == 640 && m.Height == 480 && m.FrameRate >= 30);
                                    if (preferred != null)
                                    {
                                        Logger.VideoLog.LogCall(this, $"✓ SELECTED (1st priority - preferred): {preferred.Width}x{preferred.Height}@{preferred.FrameRate}fps");
                                        return preferred;
                                    }
                                    else
                                    {
                                        Logger.VideoLog.LogCall(this, "✗ 640x480@30fps not available, trying next priority");
                                    }
                                    
                                    // 2nd priority: lowest resolution above 30fps
                                    var above30fps = availableModes
                                        .Where(m => m.FrameRate >= 30)
                                        .OrderBy(m => m.Width * m.Height)
                                        .ThenBy(m => m.FrameRate)
                                        .FirstOrDefault();
                                    if (above30fps != null)
                                    {
                                        Logger.VideoLog.LogCall(this, $"✓ SELECTED (2nd priority - lowest above 30fps): {above30fps.Width}x{above30fps.Height}@{above30fps.FrameRate}fps");
                                        return above30fps;
                                    }
                                    else
                                    {
                                        Logger.VideoLog.LogCall(this, "✗ No modes above 30fps available, trying best available");
                                    }
                                    
                                    // 3rd priority: best available resolution (highest framerate, then lowest resolution)
                                    var bestMode = availableModes
                                        .OrderByDescending(m => m.FrameRate)
                                        .ThenBy(m => m.Width * m.Height)
                                        .FirstOrDefault();
                                    if (bestMode != null)
                                    {
                                        Logger.VideoLog.LogCall(this, $"✓ SELECTED (3rd priority - best available): {bestMode.Width}x{bestMode.Height}@{bestMode.FrameRate}fps");
                                        return bestMode;
                                    }
                                }
                                else
                                {
                                    Logger.VideoLog.LogCall(this, "WARNING: No modes detected from camera");
                                }
                            }
                            else
                            {
                                Logger.VideoLog.LogCall(this, "WARNING: Frame source does not support mode detection");
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.VideoLog.LogException(this, ex);
            }
            
            Logger.VideoLog.LogCall(this, "DetectOptimalMode() returning null - no optimal mode found");
            return null;
        }

        public IEnumerable<VideoConfig> GetAvailableVideoSources()
        {
            List<VideoConfig> configs = new List<VideoConfig>();

            foreach (VideoFrameWork videoFramework in VideoFrameWorks.Available)
            {
                foreach (VideoConfig videoConfig in videoFramework.GetVideoConfigs())
                {
                    VideoConfig fromAnotherFramework = GetMatch(configs.Where(r => r.DeviceName == videoConfig.DeviceName), videoConfig.MediaFoundationPath, videoConfig.DirectShowPath);
                    if (fromAnotherFramework != null)
                    {
                        if (fromAnotherFramework.DirectShowPath == null)
                            fromAnotherFramework.DirectShowPath = videoConfig.DirectShowPath;

                        if (fromAnotherFramework.MediaFoundationPath == null)
                            fromAnotherFramework.MediaFoundationPath = videoConfig.MediaFoundationPath;
                    }
                    else
                    {
                        configs.Add(videoConfig);
                    }
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

        public IEnumerable<string> GetAvailableAudioSources()
        {
            foreach (VideoFrameWork videoFramework in VideoFrameWorks.Available)
            {
                foreach (string audioSource in videoFramework.GetAudioSources())
                {
                    yield return audioSource;
                }
            }
        }

        private VideoConfig GetMatch(IEnumerable<VideoConfig> videoConfigs, params string[] paths)
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

        public bool GetStatus(VideoConfig videoConfig, out bool connected, out bool recording, out int height)
        {
            connected = false;
            recording = false;
            height = 0;

            FrameSource frameSource = GetFrameSource(videoConfig);
            if (frameSource != null)
            {
                connected = frameSource.Connected;
                recording = frameSource.Recording;
                height = frameSource.FrameHeight;
                return true;
            }

            return false;
        }

        //public IEnumerable<VideoConfig> GetUnavailableVideoSources()
        //{
        //    foreach (DsDevice ds in DirectShowHelper.VideoCaptureDevices)
        //    {
        //        VideoConfig videoConfig = new VideoConfig() { DeviceName = ds.Name, DirectShowPath = ds.DevicePath };
        //        ds.Dispose();

        //        if (!ValidDevice(videoConfig))
        //        {
        //            yield return videoConfig;
        //        }
        //    }
        //}

        public bool ValidDevice(VideoConfig vc)
        {
            string[] whitelist = new string[]
            {
                 // OBS Virtual Camera (the old plugin one)
                "@device:sw:{860BB310-5D01-11D0-BD3B-00A0C911CE86}\\{27B05C2D-93DC-474A-A5DA-9BBA34CB2A9C}",
                "@device:sw:{860BB310-5D01-11D0-BD3B-00A0C911CE86}\\{27B05C2D-93DC-474A-A5DA-9BBA34CB2A9D}",
                "@device:sw:{860BB310-5D01-11D0-BD3B-00A0C911CE86}\\{27B05C2D-93DC-474A-A5DA-9BBA34CB2A9E}",
                "@device:sw:{860BB310-5D01-11D0-BD3B-00A0C911CE86}\\{27B05C2D-93DC-474A-A5DA-9BBA34CB2A9F}"
            };

            string[] blacklist = new string[]
            {
            };

            if (whitelist.Contains(vc.DirectShowPath))
            {
                return true;
            }

            if (blacklist.Contains(vc.DirectShowPath))
            {
                return false;
            }

            return true;
        }

        public IEnumerable<FrameSource> GetFrameSources()
        {
            foreach (VideoConfig vs in VideoConfigs)
            {
                FrameSource source = GetFrameSource(vs);
                if (source != null)
                {
                    yield return source;
                }
            }
        }

        public FrameSource GetFrameSource(VideoConfig vs)
        {
            lock (frameSources)
            {
                return frameSources.FirstOrDefault(dsss => dsss.VideoConfig == vs);
            }
        }

        private FrameSource CreateFrameSource(VideoConfig videoConfig)
        {
            if (videoConfig.DeviceName == "Static Image")
            {
                return new StaticFrameSource(videoConfig);
            }

            FrameSource source = null;
            lock (frameSources)
            {
                if (videoConfig.FilePath != null)
                {
                    try
                    {
                        if (videoConfig.FilePath.EndsWith("jpg") || videoConfig.FilePath.EndsWith("png"))
                        {
                            source = new StaticFrameSource(videoConfig);
                        }
                        else
                        {
                            // Check if this is a WMV file
                            bool isWMV = videoConfig.FilePath.EndsWith(".wmv");
                            
                            if (isWMV)
                            {
                                // Use FFmpeg for WMV files on both Windows and Mac (FFmpeg handles WMV excellently on both platforms)
                                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                                string platform = isWindows ? "Windows" : "Mac";
                                Tools.Logger.VideoLog.LogCall(this, $"Using FFmpeg for WMV playback on {platform} (original system used MediaFoundation on Windows, but FFmpeg handles WMV excellently on both platforms)");
                                
                                VideoFrameWork ffmpegFramework = VideoFrameWorks.GetFramework(FrameWork.ffmpeg);
                                if (ffmpegFramework != null)
                                {
                                    source = ffmpegFramework.CreateFrameSource(videoConfig);
                                }
                                else
                                {
                                    throw new Exception("FFmpeg framework not available for WMV playback");
                                }
                            }
                            else
                            {
                                // Use FFmpeg framework for other video file playback (MP4, MPEG-TS, etc.)
                                Tools.Logger.VideoLog.LogCall(this, "Using FFmpeg for video file playback");
                                VideoFrameWork ffmpegFramework = VideoFrameWorks.GetFramework(FrameWork.ffmpeg);
                                if (ffmpegFramework != null)
                                {
                                    source = ffmpegFramework.CreateFrameSource(videoConfig);
                                }
                                else
                                {
                                    throw new Exception("FFmpeg framework not available for video playback");
                                }
                            }
                        }

                        if (source == null)
                        {
                            throw new Exception("Invalid video/image format");
                        }
                    }
                    catch (Exception e)
                    {
                        // Failed to read the file
                        Logger.VideoLog.LogException(this, e);
                        return null;
                    }
                }
                else
                {
                    try
                    {
                        foreach (VideoFrameWork frameWork in VideoFrameWorks.Available)
                        {
                            if (videoConfig.FrameWork == frameWork.FrameWork)
                            {
                                source = frameWork.CreateFrameSource(videoConfig);
                            }
                        }
                    }
                    catch (System.Runtime.InteropServices.COMException e)
                    {
                        // Failed to load the camera..
                        Logger.VideoLog.LogException(this, e);
                        return null;
                    }
                }

                if (source != null)
                {
                    frameSources.Add(source);
                }
            }

            if (source != null)
            {
                Initialize(source);
            }

            return source;
        }

        public void RemoveFrameSource(VideoConfig videoConfig)
        {
            lock (frameSources)
            {
                FrameSource[] toRemove = frameSources.Where(fs => fs.VideoConfig == videoConfig).ToArray();

                foreach (FrameSource source in toRemove)
                {
                    Logger.VideoLog.LogCall(this, $"Immediately stopping and disposing camera '{videoConfig.DeviceName}' to free resources for potential re-add");
                    
                    try
                    {
                        // Immediately stop the frame source to release camera
                        if (source.State != FrameSource.States.Stopped)
                        {
                            source.Stop();
                        }
                        
                        // Immediately dispose to kill ffmpeg processes
                        // This ensures the camera is available for re-adding without delay
                        source.Dispose();
                        
                        Logger.VideoLog.LogCall(this, $"Camera '{videoConfig.DeviceName}' immediately stopped and disposed");
                    }
                    catch (Exception ex)
                    {
                        Logger.VideoLog.LogException(this, ex);
                        Logger.VideoLog.LogCall(this, $"Exception during immediate disposal of '{videoConfig.DeviceName}' - adding to worker thread queue as fallback");
                        
                        // If immediate disposal fails, fall back to worker thread disposal
                        DisposeOnWorkerThread(source);
                    }
                    
                    frameSources.Remove(source);
                }
            }
        }

        public bool HasReplay(Race currentRace)
        {
            if (currentRace != null)
            {
                var recordings = GetRecordings(currentRace);
                bool hasRecordings = recordings.Any();
                Tools.Logger.VideoLog.LogCall(this, $"HasReplay check for race {currentRace.ID} - Found {recordings.Count()} recordings: {hasRecordings}");
                
                // Log the first few recordings for debugging
                foreach (var recording in recordings.Take(3))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"  Recording: {recording.DeviceName}, FilePath: {recording.FilePath}");
                }
                
                return hasRecordings;
            }
            Tools.Logger.VideoLog.LogCall(this, "HasReplay called with null race");
            return false;
        }

        public IEnumerable<ChannelVideoInfo> CreateChannelVideoInfos()
        {
            return CreateChannelVideoInfos(VideoConfigs);
        }

        public IEnumerable<ChannelVideoInfo> CreateChannelVideoInfos(IEnumerable<VideoConfig> videoSources)
        {
            List<ChannelVideoInfo> channelVideoInfos = new List<ChannelVideoInfo>();
            foreach (VideoConfig videoConfig in videoSources)
            {
                foreach (VideoBounds videoBounds in videoConfig.VideoBounds)
                {
                    FrameSource source = null;
                    try
                    {
                        source = GetFrameSource(videoConfig);
                    }
                    catch (System.Runtime.InteropServices.COMException e)
                    {
                        // Failed to load the camera..
                        Logger.VideoLog.LogException(this, e);
                    }
                    if (source != null)
                    {
                        Channel channel = videoBounds.GetChannel();
                        if (channel == null)
                        {
                            channel = Channel.None;
                        }

                        ChannelVideoInfo cvi = new ChannelVideoInfo(videoBounds, channel, source);
                        channelVideoInfos.Add(cvi);
                    }
                }
            }

            return channelVideoInfos;
        }


        public void StartRecording(Race race)
        {
            this.race = race;
            lock (recording)
            {
                foreach (ICaptureFrameSource source in frameSources.OfType<ICaptureFrameSource>().Where(r => r.VideoConfig.RecordVideoForReplays))
                {
                    // if all feeds on this source are FPV, only record if they're visible..
                    if (source.VideoConfig.VideoBounds.All(r => r.SourceType == SourceTypes.FPVFeed))
                    {
                        if (source.IsVisible)
                        {
                            recording.Add(source);
                        }
                    }
                    else
                    {
                        // record
                        recording.Add(source);
                    }
                }
            }
            mutex.Set();
        }

        public Mode PickMode(VideoConfig videoConfig, IEnumerable<Mode> modes)
        {
            VideoFrameWork videoFrameWork = GetFramework(videoConfig.FrameWork);
            if (videoFrameWork != null)
            {
                return videoFrameWork.PickMode(modes);
            }
            return modes.FirstOrDefault();
        }

        public void StopRecording()
        {
            lock (recording)
            {
                recording.Clear();
            }
            mutex.Set();
        }

        private string GetRecordingFilename(Race race, FrameSource source)
        {
            return Path.Combine(EventDirectory.FullName, race.ID.ToString(), source.VideoConfig.ffmpegId) + ".mp4";
        }

        public void LoadRecordings(Race race, FrameSourcesDelegate frameSourcesDelegate)
        {
            MaintainConnections = false;

            VideoConfigs.Clear();
            VideoConfigs.AddRange(GetRecordings(race));
            CreateFrameSource(VideoConfigs, frameSourcesDelegate);
            StartThread();
        }

        public IEnumerable<VideoConfig> GetRecordings(Race race)
        {
            DirectoryInfo raceDirectory = new DirectoryInfo(Path.Combine(EventDirectory.FullName, race.ID.ToString()));
            Tools.Logger.VideoLog.LogCall(this, $"GetRecordings for race {race.ID} - Directory: {raceDirectory.FullName}, Exists: {raceDirectory.Exists}");
            
            if (raceDirectory.Exists)
            {
                var recordInfoFiles = raceDirectory.GetFiles("*.recordinfo.xml");
                Tools.Logger.VideoLog.LogCall(this, $"Found {recordInfoFiles.Length} .recordinfo.xml files");
                
                foreach (FileInfo file in recordInfoFiles)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Processing recordinfo file: {file.Name}");
                    RecodingInfo videoInfo = null;

                    try
                    {
                        videoInfo = IOTools.ReadSingle<RecodingInfo>(raceDirectory.FullName, file.Name);
                        Tools.Logger.VideoLog.LogCall(this, $"Successfully read recordinfo: FilePath={videoInfo?.FilePath}");
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, ex);
                    }

                    if (videoInfo != null)
                    {
                        // Resolve the relative path to an absolute path for file existence check
                        string absoluteVideoPath = Path.GetFullPath(videoInfo.FilePath);
                        bool videoFileExists = File.Exists(absoluteVideoPath);
                        Tools.Logger.VideoLog.LogCall(this, $"Video file exists: {videoFileExists} - {videoInfo.FilePath} (resolved to: {absoluteVideoPath})");
                        
                        if (videoFileExists)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Yielding video config for: {videoInfo.FilePath}");
                            yield return videoInfo.GetVideoConfig();
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Video file not found: {videoInfo.FilePath} (resolved to: {absoluteVideoPath})");
                        }
                    }
                }
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, $"Race directory does not exist: {raceDirectory.FullName}");
            }
        }

        public void CreateFrameSource(IEnumerable<VideoConfig> videoConfigs, FrameSourcesDelegate frameSourcesDelegate)
        {
            List<FrameSource> list = new List<FrameSource>();
            foreach (var videoConfig in videoConfigs)
            {
                RemoveFrameSource(videoConfig);
                FrameSource fs = CreateFrameSource(videoConfig);
                list.Add(fs);
                
                // For resolution changes, ensure immediate camera restart with new settings
                if (fs != null)
                {
                    Logger.VideoLog.LogCall(this, $"Resolution change detected for '{videoConfig.DeviceName}' - forcing immediate clean restart");
                    
                    // Force immediate initialization by adding to queue and waking up worker thread
                    Initialize(fs);
                    
                    // Wake up worker thread immediately to process the initialization
                    // This prevents delays when resolution changes
                    mutex.Set();
                }
            }

            DoOnWorkerThread(() =>
            {
                if (frameSourcesDelegate != null)
                {
                    frameSourcesDelegate(list);
                }
            });
        }

        public void Initialize(FrameSource frameSource)
        {
            lock (needsInitialize)
            {
                if (!needsInitialize.Contains(frameSource))
                {
                    needsInitialize.Add(frameSource);
                }
            }

            mutex.Set();
        }

        public void CheckFileCount()
        {
            try
            {
                int maxCount = ApplicationProfileSettings.Instance.VideosToKeep;
                EventDirectory.Refresh();

                FileInfo[] files = AllEventAllVideoFiles().ToArray();

                int toDelete = files.Count() - maxCount;

                if (toDelete > 0)
                {
                    IEnumerable<FileInfo> delete = files.OrderBy(r => r.LastWriteTime).Take(toDelete);
                    foreach (FileInfo file in delete)
                    {
                        file.Delete();

                        FileInfo xmlconfig = new FileInfo(file.FullName.Replace(".wmv", ".recordinfo.xml"));
                        if (xmlconfig.Exists)
                        {
                            xmlconfig.Delete();
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private IEnumerable<FileInfo> AllEventAllVideoFiles()
        {
            if (EventDirectory == null)
                yield break;

            DirectoryInfo parentDirectory = EventDirectory.Parent;

            foreach (DirectoryInfo eventDir in parentDirectory.EnumerateDirectories())
            {
                foreach (DirectoryInfo raceDir in eventDir.EnumerateDirectories())
                {
                    foreach (FileInfo fileInfo in raceDir.GetFiles("*.wmv"))
                    {
                        yield return fileInfo;
                    }

                    foreach (FileInfo fileInfo in raceDir.GetFiles("*.mp4"))
                    {
                        yield return fileInfo;
                    }
                }
            }
        }

        public void UpdateAutoPause()
        {
            if (!AutoPause)
                return;

            lock (frameSources)
            {
                foreach (FrameSource frameSource in frameSources)
                {
                    //apply the previous frames visiblity
                    if (frameSource.IsVisible != frameSource.DrawnThisGraphicsFrame)
                    {
                        frameSource.IsVisible = frameSource.DrawnThisGraphicsFrame;
                        mutex.Set();
                    }

                    // Clears the draw flag
                    frameSource.DrawnThisGraphicsFrame = false;
                }
            }
        }

        private void WorkerThread(CancellationToken cancellationToken)
        {
            bool someFinalising = false;

            List<FrameSource> needsVideoInfoWrite = new List<FrameSource>();
            while (runWorker && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for a set on the mutex or just every X ms
                    if (!mutex.WaitOne(someFinalising ? 500 : 4000))
                    {
                        
                    }

                    if (!runWorker || cancellationToken.IsCancellationRequested)
                        break;

                    if (someFinalising)
                    {
                        if (!Finalising)
                        {
                            someFinalising = false;
                            OnFinishedFinalizing();
                        }
                    }

                    bool doCountClean = false;
                    lock (recording)
                    {
                        try
                        {
                            foreach (ICaptureFrameSource source in recording)
                            {
                                if (!source.Recording)
                                {
                                    FrameSource frameSource = source as FrameSource;
                                    if (frameSource != null)
                                    {
                                        if (frameSource.State == FrameSource.States.Paused)
                                        {
                                            frameSource.Unpause();
                                        }
                                    }
                                    
                                    string filename = GetRecordingFilename(race, (FrameSource)source);
                                    source.StartRecording(filename);
                                    doCountClean = true;

                                    needsVideoInfoWrite.Add((FrameSource)source);
                                }

                                source.RecordNextFrameTime = true;
                            }

                            if (!recording.Any())
                            {
                                ICaptureFrameSource[] stopRecording;

                                lock (frameSources)
                                {
                                    stopRecording = frameSources.OfType<ICaptureFrameSource>().Where(r => !r.ManualRecording).ToArray();
                                }

                                foreach (ICaptureFrameSource source in stopRecording)
                                {
                                    if (source.Recording)
                                    {
                                        source.StopRecording();
                                        someFinalising = true;
                                    }

                                    needsVideoInfoWrite.Add((FrameSource)source);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.VideoLog.LogException(this, e);
                        }
                    }

                    if (needsVideoInfoWrite.Any())
                    {
                        ICaptureFrameSource[] sources = needsVideoInfoWrite.OfType<ICaptureFrameSource>().Where(r => r.VideoConfig.RecordVideoForReplays).ToArray();
                        foreach (ICaptureFrameSource source in sources)
                        {
                            try
                            {
                                if (source.FrameTimes != null && source.FrameTimes.Any())
                                {
                                    RecodingInfo vi = new RecodingInfo(source);

                                    // Handle both .wmv and .mp4 file extensions for metadata files
                                    string basePath = vi.FilePath;
                                    if (basePath.EndsWith(".wmv"))
                                    {
                                        basePath = basePath.Replace(".wmv", "");
                                    }
                                    else if (basePath.EndsWith(".mp4"))
                                    {
                                        basePath = basePath.Replace(".mp4", "");
                                    }
                                    else if (basePath.EndsWith(".ts"))
                                    {
                                        basePath = basePath.Replace(".ts", "");
                                    }
                                    
                                    FileInfo fileinfo = new FileInfo(basePath + ".recordinfo.xml");
                                    IOTools.Write(fileinfo.Directory.FullName, fileinfo.Name, vi);
                                    needsVideoInfoWrite.Remove((FrameSource)source);
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.VideoLog.LogException(this, e);
                            }
                        }
                    }

                    FrameSource[] initalizeNow = new FrameSource[0];
                    
                    lock (needsInitialize)
                    {
                        lock (frameSources)
                        {
                            if (MaintainConnections)
                            {
                                needsInitialize.AddRange(frameSources.Where(r => !r.Connected));
                            }
                        }

                        initalizeNow = needsInitialize.Distinct().ToArray();
                        needsInitialize.Clear();
                    }

                    foreach (FrameSource frameSource in initalizeNow)
                    {
                        try
                        {
                            if (frameSource.State != FrameSource.States.Stopped)
                            {
                                frameSource.Stop();
                            }

                            frameSource.CleanUp();

                            bool result = false;
                            bool shouldStart = false;
                            lock (frameSources)
                            {
                                shouldStart = frameSources.Contains(frameSource);
                            }

                            if (shouldStart)
                            {
                                result = frameSource.Start();
                            }

                            if (result)
                            {
                                OnStart?.Invoke(frameSource);
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.VideoLog.LogException(this, e);
                        }
                    }

                    if (AutoPause && !initalizeNow.Any())
                    {
                        FrameSource[] toPause;
                        FrameSource[] toResume;
                        lock (frameSources)
                        {
                            toPause = frameSources.Where(r => !r.IsVisible && r.State == FrameSource.States.Running && r.VideoConfig.Pauseable && !r.Recording).ToArray();
                            toResume = frameSources.Where(r => r.IsVisible && r.State == FrameSource.States.Paused).ToArray();
                        }

                        foreach (FrameSource fs in toPause)
                        {
                            fs.Pause();
                        }

                        foreach (FrameSource fs in toResume)
                        {
                            fs.Unpause();
                        }
                    }

                    if (doCountClean)
                    {
                        CheckFileCount();
                    }

                    // Run any clean up tasks..
                    WorkerThreadCleanupTasks();

                }
                catch (Exception e)
                {
                    Logger.VideoLog.LogException(this, e);
                }
            }

            try
            {
                // Remove any zombies sitting around..
                Clear();

                // Run the clean up tasks one last time...
                WorkerThreadCleanupTasks();
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
            }
        }

        private void WorkerThreadCleanupTasks()
        {
            lock (needsDispose)
            {
                // We want to do the join in parallel, so stop all teh worker threads in these first.
                foreach (TextureFrameSource textureFrameSource in needsDispose.OfType<TextureFrameSource>())
                {
                    textureFrameSource.StopProcessing();
                }

                foreach (IDisposable fs in needsDispose)
                {
                    fs.Dispose();
                }
                needsDispose.Clear();
            }

            lock (todo)
            {
                try
                {
                    foreach (var action in todo)
                    {
                        action();
                    }
                    todo.Clear();
                }
                catch (Exception e)
                {
                    Logger.VideoLog.LogException(this, e);
                }
            }
        }

        public void GetModes(VideoConfig vs, bool forceAll, Action<ModesResult> callback)
        {
            DoOnWorkerThread(() =>
            {
                ModesResult result = new ModesResult();
                result.Modes = new Mode[0];
                result.RebootRequired = false;

                try
                {
                    List<Mode> modes = new List<Mode>();
                    IHasModes frameSource = GetFrameSource(vs) as IHasModes;
                    if (frameSource != null && !forceAll)
                    {
                        modes.AddRange(frameSource.GetModes());
                    }
                    else
                    {
                        // Stop any existing frame source for this camera to avoid access conflicts
                        FrameSource existingSource = GetFrameSource(vs);
                        if (existingSource != null)
                        {
                            Logger.VideoLog.LogCall(this, $"Stopping existing frame source for '{vs.DeviceName}' to query modes");
                            existingSource.Stop();
                            // Small delay to ensure camera is fully released
                            System.Threading.Thread.Sleep(500);
                        }

                        // Clear the video mode so it's not a problem getting new modes if the current one doesnt work?
                        VideoConfig clone = vs.Clone();
                        clone.VideoMode = new Mode();

                        foreach (VideoFrameWork frameWork in VideoFrameWorks.Available)
                        {
                            if (clone.FrameWork == frameWork.FrameWork)
                            {
                                // Create a temporary instance just to get the modes...
                                using (FrameSource source = frameWork.CreateFrameSource(clone))
                                {
                                    modes.AddRange(source.GetModes());
                                    if (source.RebootRequired)
                                    {
                                        result.RebootRequired = true;
                                    }
                                }
                            }
                        }
                        
                        // Restart the existing source if it was running
                        if (existingSource != null)
                        {
                            Logger.VideoLog.LogCall(this, $"Restarting frame source for '{vs.DeviceName}' after mode query");
                            Initialize(existingSource);
                        }
                    }

                    result.Modes = modes.Distinct().ToArray();
                    callback(result);
                }
                catch
                {
                    callback(result);
                }
            });
        }

        public struct ModesResult
        {
            public Mode[] Modes { get; set; }
            public bool RebootRequired { get; set; }
        }
    }

    public static class VideoManagerFactory
    {
        private static string directory;
        private static Profile profile;

        public static void Init(string di, Profile p)
        {
            directory = di;
            profile = p;
        }

        public static VideoManager CreateVideoManager()
        {
            return new VideoManager(directory, profile);
        }
    }
}
