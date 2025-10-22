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
            VideoConfigs.AddRange(VideoConfig.Read(Profile));

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

            // For FPV feeds, check if they're configured and device is available (not necessarily streaming)
            bool isFPVFeed = videoConfig.VideoBounds.Any(vb => vb.SourceType == SourceTypes.FPVFeed);
            if (isFPVFeed)
            {
                // FPV monitor should show green if cameras are configured and device exists
                connected = !string.IsNullOrEmpty(videoConfig.DeviceName) && videoConfig.DeviceName != "No Device";
                recording = false; // FPV monitors don't show recording status
                height = 480; // Default height for status display
                Tools.Logger.VideoLog.LogCall(this, $"GetStatus: FPV feed status - Device: '{videoConfig.DeviceName}', Connected: {connected}");
                return true;
            }

            // For non-FPV feeds, check actual frame source connection
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
                // First try exact object match
                FrameSource existing = frameSources.FirstOrDefault(dsss => dsss.VideoConfig == vs);
                if (existing != null)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"GetFrameSource: Found existing source by object reference {existing.GetType().Name} (Instance: {existing.GetHashCode()}) for device: {vs.DeviceName}");
                    return existing;
                }
                
                // Fall back to device name match (for FPV monitor to share with race channels)
                Tools.Logger.VideoLog.LogCall(this, $"GetFrameSource: Looking for device name match, target: '{vs.DeviceName}', existing sources: {frameSources.Count}");
                foreach (var source in frameSources)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"GetFrameSource: Comparing '{vs.DeviceName}' with '{source.VideoConfig.DeviceName}'");
                }
                existing = frameSources.FirstOrDefault(dsss => dsss.VideoConfig.DeviceName == vs.DeviceName);
                if (existing != null)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"GetFrameSource: Found existing source by device name {existing.GetType().Name} (Instance: {existing.GetHashCode()}) for device: {vs.DeviceName}");
                    return existing;
                }
                Tools.Logger.VideoLog.LogCall(this, $"GetFrameSource: No device name match found for '{vs.DeviceName}'");
                
                // Create new frame source if not found
                Tools.Logger.VideoLog.LogCall(this, $"GetFrameSource: Creating new source for device: {vs.DeviceName}");
                FrameSource newSource = CreateFrameSource(vs);
                if (newSource != null)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"GetFrameSource: Created new source {newSource.GetType().Name} (Instance: {newSource.GetHashCode()}) for device: {vs.DeviceName}");
                }
                return newSource;
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
                                VideoFrameWork videoFramework = VideoFrameWorks.GetFramework(FrameWork.ffmpeg, FrameWork.MediaFoundation, FrameWork.DirectShow);
                                if (videoFramework != null)
                                {
                                    Tools.Logger.VideoLog.LogCall(this, $"Using {videoFramework.GetName()} for WMV playback");
                                    source = videoFramework.CreateFrameSource(videoConfig);
                                }
                                else
                                {
                                    throw new Exception("No framework not available for WMV playback");
                                }
                            }
                            else
                            {
                                // Use FFmpeg framework for other video file playback (MP4, MPEG-TS, etc.)
                                VideoFrameWork videoFramework = VideoFrameWorks.GetFramework(FrameWork.ffmpeg, FrameWork.MediaFoundation, FrameWork.DirectShow);
                                if (videoFramework != null)
                                {
                                    Tools.Logger.VideoLog.LogCall(this, $"Using {videoFramework.GetName()} for (MP4, MPEG-TS, etc.) playback");
                                    source = videoFramework.CreateFrameSource(videoConfig);
                                }
                                else
                                {
                                    throw new Exception("No framework not available for video playback");
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
                    if (source.VideoConfig.FrameWork == FrameWork.DirectShow || source.VideoConfig.FrameWork == FrameWork.MediaFoundation)
                    {
                        DisposeOnWorkerThread(source);
                    }
                    else
                    {
                        Logger.VideoLog.LogCall(this, $"Immediately stopping and disposing camera '{videoConfig.DeviceName}' to free resources for potential re-add");

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
            Tools.Logger.VideoLog.LogCall(this, $"CreateChannelVideoInfos called with {videoSources?.Count() ?? 0} video sources");
            
            List<ChannelVideoInfo> channelVideoInfos = new List<ChannelVideoInfo>();
            foreach (VideoConfig videoConfig in videoSources)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Processing VideoConfig: {videoConfig.DeviceName}, VideoBounds count: {videoConfig.VideoBounds?.Length ?? 0}");
                
                foreach (VideoBounds videoBounds in videoConfig.VideoBounds)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Processing VideoBounds: SourceType={videoBounds.SourceType}, Channel={videoBounds.GetChannel()?.ToString() ?? "null"}");
                    
                    FrameSource source = null;
                    try
                    {
                        source = GetFrameSource(videoConfig);
                        Tools.Logger.VideoLog.LogCall(this, $"GetFrameSource returned: {source?.GetType()?.Name ?? "null"} (Instance: {source?.GetHashCode()})");
                    }
                    catch (System.Runtime.InteropServices.COMException e)
                    {
                        // Failed to load the camera..
                        Logger.VideoLog.LogException(this, e);
                        Tools.Logger.VideoLog.LogCall(this, $"COM Exception getting frame source: {e.Message}");
                    }
                    catch (Exception e)
                    {
                        Logger.VideoLog.LogException(this, e);
                        Tools.Logger.VideoLog.LogCall(this, $"Exception getting frame source: {e.Message}");
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
                        Tools.Logger.VideoLog.LogCall(this, $"Created ChannelVideoInfo: Channel={channel}, FrameSource={source.GetType().Name} (Instance: {source.GetHashCode()})");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "No frame source - skipping ChannelVideoInfo creation");
                    }
                }
            }

            Tools.Logger.VideoLog.LogCall(this, $"CreateChannelVideoInfos completed - returning {channelVideoInfos.Count} ChannelVideoInfos");
            return channelVideoInfos;
        }


        public void StartRecording(Race race)
        {
            this.race = race;
            Tools.Logger.VideoLog.LogCall(this, $"StartRecording called for race: {race?.ToString() ?? "null"}");
            
            lock (recording)
            {
                var captureFrameSources = frameSources.OfType<ICaptureFrameSource>().ToList();
                Tools.Logger.VideoLog.LogCall(this, $"Found {captureFrameSources.Count} ICaptureFrameSource instances");
                
                var recordingSources = captureFrameSources.Where(r => r.VideoConfig.RecordVideoForReplays).ToList();
                Tools.Logger.VideoLog.LogCall(this, $"Found {recordingSources.Count} sources with RecordVideoForReplays=true");
                
                foreach (ICaptureFrameSource source in recordingSources)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Evaluating source: {source.GetType().Name} (Instance: {source.GetHashCode()})");
                    Tools.Logger.VideoLog.LogCall(this, $"  - IsVisible: {source.IsVisible}");
                    Tools.Logger.VideoLog.LogCall(this, $"  - VideoBounds count: {source.VideoConfig?.VideoBounds?.Length ?? 0}");
                    
                    if (source.VideoConfig?.VideoBounds != null)
                    {
                        var sourceTypes = source.VideoConfig.VideoBounds.Select(vb => vb.SourceType).ToArray();
                        Tools.Logger.VideoLog.LogCall(this, $"  - Source types: [{string.Join(", ", sourceTypes)}]");
                        bool allFPV = source.VideoConfig.VideoBounds.All(r => r.SourceType == SourceTypes.FPVFeed);
                        Tools.Logger.VideoLog.LogCall(this, $"  - All FPV feeds: {allFPV}");
                    }
                    
                    // if all feeds on this source are FPV, only record if they're visible..
                    if (source.VideoConfig.VideoBounds.All(r => r.SourceType == SourceTypes.FPVFeed))
                    {
                        if (source.IsVisible)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"  - Adding FPV source to recording (visible)");
                            recording.Add(source);
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"  - Skipping FPV source (not visible)");
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"  - Adding non-FPV source to recording");
                        recording.Add(source);
                    }
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"Recording collection now has {recording.Count} sources");
            }
            mutex.Set();
        }

        public Mode PickMode(VideoConfig videoConfig, IEnumerable<Mode> modes)
        {
            VideoFrameWork videoFrameWork = VideoFrameWorks.GetFramework(videoConfig.FrameWork);
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
            // Use .mkv for better seekability and timestamp preservation as per specification
            return Path.Combine(EventDirectory.FullName, race.ID.ToString(), source.VideoConfig.ffmpegId) + ".mkv";
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

                        // Handle different video file extensions for XML cleanup
                        string xmlPath = file.FullName;
                        if (xmlPath.EndsWith(".wmv"))
                        {
                            xmlPath = xmlPath.Replace(".wmv", ".recordinfo.xml");
                        }
                        else if (xmlPath.EndsWith(".mp4"))
                        {
                            xmlPath = xmlPath.Replace(".mp4", ".recordinfo.xml");
                        }
                        else if (xmlPath.EndsWith(".mkv"))
                        {
                            xmlPath = xmlPath.Replace(".mkv", ".recordinfo.xml");
                        }
                        
                        FileInfo xmlconfig = new FileInfo(xmlPath);
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

                    foreach (FileInfo fileInfo in raceDir.GetFiles("*.mkv"))
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
                                    Tools.Logger.VideoLog.LogCall(this, $"Calling StartRecording on {source.GetType().Name} (Instance: {source.GetHashCode()}) with filename: {filename}");
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
                                // Check if this is an RGBA recording source
                                bool isRgbaRecording = source.GetType().Name.Contains("Composite") || source.GetType().Name.Contains("Hls");
                                
                                if (isRgbaRecording)
                                {
                                    // For RGBA recording, XML is now generated by RgbaRecorderManager from camera loop
                                    // No need to generate it here to avoid duplicates
                                    Tools.Logger.VideoLog.LogCall(this, $"RGBA recording detected for {source.GetType().Name} - XML metadata handled by camera loop");
                                    needsVideoInfoWrite.Remove((FrameSource)source);
                                }
                                else
                                {
                                    // For non-RGBA recording, use existing logic
                                    bool canGenerateXml = source.FrameTimes != null && source.FrameTimes.Any();
                                    
                                    if (canGenerateXml)
                                    {
                                        Tools.Logger.VideoLog.LogCall(this, $"Generating XML for {source.GetType().Name} with {source.FrameTimes.Length} frame times");
                                        
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
                                        else if (basePath.EndsWith(".mkv"))
                                        {
                                            basePath = basePath.Replace(".mkv", "");
                                        }
                                        
                                        FileInfo fileinfo = new FileInfo(basePath + ".recordinfo.xml");
                                        IOTools.Write(fileinfo.Directory.FullName, fileinfo.Name, vi);
                                        needsVideoInfoWrite.Remove((FrameSource)source);
                                        
                                        Tools.Logger.VideoLog.LogCall(this, $"Generated XML file: {fileinfo.FullName} with {source.FrameTimes.Length} frame times");
                                    }
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
