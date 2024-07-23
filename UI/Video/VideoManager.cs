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
            return VideoFrameworks.Available.FirstOrDefault(f => f.FrameWork == frameWork);
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
                videoDeviceManagerThread = new Thread(WorkerThread);
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
            Clear();
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

            if (videoDeviceManagerThread != null)
            {
                if (!videoDeviceManagerThread.Join(30000))
                {
                    try
                    {
#pragma warning disable SYSLIB0006 // Type or member is obsolete
                        videoDeviceManagerThread.Abort();
#pragma warning restore SYSLIB0006 // Type or member is obsolete
                    }
                    catch
                    {
                    }
                }
                videoDeviceManagerThread.Join();
                videoDeviceManagerThread = null;
            }
        }

        public void Clear()
        {
            Logger.VideoLog.LogCall(this);
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

            foreach (VideoFrameWork videoFramework in VideoFrameworks.Available)
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
                            VideoFrameWork mediaFoundation = VideoFrameworks.GetFramework(FrameWork.MediaFoundation);
                            if (mediaFoundation != null)
                            {
                                source = mediaFoundation.CreateFrameSource(videoConfig);
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
                        foreach (VideoFrameWork frameWork in VideoFrameworks.Available)
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
                    DisposeOnWorkerThread(source);
                    frameSources.Remove(source);
                }
            }
        }

        public bool HasReplay(Race currentRace)
        {
            if (currentRace != null)
            {
                return GetRecordings(currentRace).Any();
            }
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
            int index = frameSources.IndexOf(source);
            return Path.Combine(EventDirectory.FullName, race.ID.ToString(), index.ToString());
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
            if (raceDirectory.Exists)
            {
                foreach (FileInfo file in raceDirectory.GetFiles("*.recordinfo.xml"))
                {
                    RecodingInfo videoInfo = IOTools.ReadSingle<RecodingInfo>(raceDirectory.FullName, file.Name);
                    if (videoInfo != null)
                    {
                        if (File.Exists(videoInfo.FilePath))
                        {
                            yield return videoInfo.GetVideoConfig();
                        }
                    }
                }
            }
        }

        public void CreateFrameSource(IEnumerable<VideoConfig> videoConfigs, FrameSourcesDelegate frameSourcesDelegate)
        {
            List<FrameSource> list = new List<FrameSource>();
            foreach (var videoConfig in videoConfigs)
            {
                RemoveFrameSource(videoConfig);

                FrameSource fs = CreateFrameSource(videoConfig);
                if (fs != null)
                {
                    Initialize(fs);
                }
                list.Add(fs);
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

        private void CheckFileCount()
        {
            try
            {
                int maxCount = ApplicationProfileSettings.Instance.VideosToKeep;
                EventDirectory.Refresh();

                FileInfo[] files = EventDirectory.GetFiles("*.wmv");

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

        private void WorkerThread()
        {
            bool someFinalising = false;

            List<FrameSource> needsVideoInfoWrite = new List<FrameSource>();
            while (runWorker)
            {
                try
                {
                    // Wait for a set on the mutex or just every X ms
                    if (!mutex.WaitOne(someFinalising ? 500 : 4000))
                    {
                        
                    }

                    if (!runWorker)
                        break;

                    // Run any clean up tasks..
                    WorkerThreadCleanupTasks();

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

                                    FileInfo fileinfo = new FileInfo(vi.FilePath.Replace(".wmv", "") + ".recordinfo.xml");
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
                        // Clear the video mode so it's not a problem getting new modes if the current one doesnt work?
                        VideoConfig clone = vs.Clone();
                        clone.VideoMode = new Mode();

                        foreach (VideoFrameWork frameWork in VideoFrameworks.Available)
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
