using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timing;
using Tools;

namespace ExternalData
{
    // ExtensionNotifier
    //
    // Active only when ApplicationProfileSettings.ExtensionMode = true.
    // Independent of the legacy RemoteNotifier (which is preserved verbatim).
    // Wire format and semantics: see FT_extensions/INTERFACE.en.md / INTERFACE.ja.md.
    public class ExtensionNotifier : IDisposable
    {
        private const int HttpQueueCapacity = 200;
        private const int SerialQueueCapacity = 50;
        private const int HttpTimeoutMs = 1500;
        private const int SerialWriteTimeoutMs = 100;
        private const int HelloIntervalMs = 2000;
        private const int RecentDetectionIdsCap = 1024;

        private readonly EventManager eventManager;
        private readonly string url;
        private readonly string comportname;
        private readonly Profile profile;
        private readonly string eventStorageLocation;
        private readonly int decimalPlaces;

        private WorkQueue httpQueue;
        private WorkQueue serialQueue;
        private int httpQueueSize;
        private int serialQueueSize;

        private HttpClient httpClient;
        private SerialPort serialPort;

        private long seqCounter;

        private readonly object detectionLock = new object();
        private readonly HashSet<Guid> recentDetectionIds = new HashSet<Guid>();
        private readonly Queue<Guid> recentDetectionOrder = new Queue<Guid>();

        private Timer helloTimer;
        private volatile bool helloAcknowledged;
        private volatile bool helloLogged;
        private volatile bool disposed;

        private Type lastHttpExceptionType;
        private Type lastSerialExceptionType;

        private readonly JsonSerializerSettings serializerSettings;

        public ExtensionNotifier(EventManager eventManager, string url, string comportname, Profile profile, string eventStorageLocation, int decimalPlaces)
        {
            this.eventManager = eventManager;
            this.url = url;
            this.comportname = comportname;
            this.profile = profile;
            this.eventStorageLocation = eventStorageLocation;
            this.decimalPlaces = decimalPlaces;

            serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ",
                NullValueHandling = NullValueHandling.Include,
                // Wire format is camelCase. C# DTOs keep PascalCase property names
                // (CLR convention); the resolver lowercases the first letter on serialize.
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };

            httpQueue = new WorkQueue("ExtensionNotifier-HTTP");
            serialQueue = new WorkQueue("ExtensionNotifier-Serial");

            if (!string.IsNullOrEmpty(url))
            {
                // Manage the client-side connection pool ourselves so that idle
                // connections are recycled BEFORE any receiver-side keep-alive
                // timeout fires. 5 minutes covers a full race (max race length)
                // so a single connection serves all detections in one race.
                // Receivers MUST set their keep-alive timeout HIGHER than this
                // (the test receiver uses 6 minutes).
                var handler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                    UseProxy = false,
                    AllowAutoRedirect = false
                };
                httpClient = new HttpClient(handler, disposeHandler: true)
                {
                    Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs),
                    DefaultRequestVersion = System.Net.HttpVersion.Version11,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                };
                // We do NOT set ConnectionClose=true — letting the pool reuse fresh
                // connections is faster, and the 2s idle timeout above guarantees
                // we never reuse a stale connection past the receiver's keep-alive.
            }

            string[] ports = SerialPort.GetPortNames();
            if (!string.IsNullOrEmpty(comportname) && ports.Contains(comportname))
            {
                try
                {
                    serialPort = new SerialPort
                    {
                        BaudRate = 115200,
                        RtsEnable = true,
                        DtrEnable = true,
                        WriteTimeout = SerialWriteTimeoutMs,
                        ReadTimeout = 1,
                        PortName = comportname
                    };
                    serialPort.Open();
                }
                catch (Exception ex)
                {
                    Logger.HTTP.LogException(this, ex);
                    serialPort = null;
                }
            }

            SubscribeRaceManager();
            SubscribeResultManager();

            StartHelloHeartbeat();
        }

        private void SubscribeRaceManager()
        {
            RaceManager rm = eventManager.RaceManager;
            rm.OnRaceChanged += RaceManager_OnRaceChanged;
            // OnRaceReset fires when the operator clears results (RaceManager.ResetRace).
            // OnRaceChanged stays silent if the same race is then re-selected, so wire
            // the same handler here to resupply RaceLoaded + NextRace as a fresh state
            // snapshot. Spec §7.1: consecutive same-round/race RaceLoaded is normal.
            rm.OnRaceReset += RaceManager_OnRaceChanged;
            rm.OnRaceStartScheduled += RaceManager_OnRaceStartScheduled;
            rm.OnRaceStart += RaceManager_OnRaceStart;
            rm.OnRaceEnd += RaceManager_OnRaceEnd;
            rm.OnRaceCancelled += RaceManager_OnRaceCancelled;
            rm.OnRaceTimesUp += RaceManager_OnRaceTimesUp;
            rm.OnSplitDetection += RaceManager_OnDetection;
            rm.OnLapDetected += RaceManager_OnLapDetected;
            rm.OnPilotAdded += RaceManager_OnPilotsChanged;
            rm.OnPilotRemoved += RaceManager_OnPilotsChanged;
            rm.OnChannelCrashedOut += RaceManager_OnChannelCrashedOut;
            rm.OnPilotStartStaggered += RaceManager_OnPilotStartStaggered;
        }

        private void UnsubscribeRaceManager()
        {
            RaceManager rm = eventManager?.RaceManager;
            if (rm == null) return;
            rm.OnRaceChanged -= RaceManager_OnRaceChanged;
            rm.OnRaceReset -= RaceManager_OnRaceChanged;
            rm.OnRaceStartScheduled -= RaceManager_OnRaceStartScheduled;
            rm.OnRaceStart -= RaceManager_OnRaceStart;
            rm.OnRaceEnd -= RaceManager_OnRaceEnd;
            rm.OnRaceCancelled -= RaceManager_OnRaceCancelled;
            rm.OnRaceTimesUp -= RaceManager_OnRaceTimesUp;
            rm.OnSplitDetection -= RaceManager_OnDetection;
            rm.OnLapDetected -= RaceManager_OnLapDetected;
            rm.OnPilotAdded -= RaceManager_OnPilotsChanged;
            rm.OnPilotRemoved -= RaceManager_OnPilotsChanged;
            rm.OnChannelCrashedOut -= RaceManager_OnChannelCrashedOut;
            rm.OnPilotStartStaggered -= RaceManager_OnPilotStartStaggered;
        }

        private void SubscribeResultManager()
        {
            ResultManager rs = eventManager?.ResultManager;
            if (rs != null)
            {
                rs.RaceResultsChanged += ResultManager_OnRaceResultsChanged;
            }
        }

        private void UnsubscribeResultManager()
        {
            ResultManager rs = eventManager?.ResultManager;
            if (rs != null)
            {
                rs.RaceResultsChanged -= ResultManager_OnRaceResultsChanged;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            try { helloTimer?.Dispose(); } catch { }
            helloTimer = null;

            UnsubscribeRaceManager();
            UnsubscribeResultManager();

            // Drop queued items before WorkQueue.Dispose() runs — its
            // thread.Join() would otherwise wait while the worker drains every
            // pending HTTP PUT at up to 1500 ms apiece. With an unreachable
            // receiver and a race-end backlog (RaceEnd / RaceResult /
            // StageRanking / trailing DetectionExt), that freezes "Back to
            // event selection" for several seconds. Clearing first bounds the
            // wait to at most one in-flight request. Per spec §9 the sender
            // does not retry on failure, so dropping the tail is acceptable.
            try { httpQueue?.Clear(); } catch { }
            try { serialQueue?.Clear(); } catch { }

            try { serialPort?.Close(); } catch { }
            try { serialPort?.Dispose(); } catch { }
            serialPort = null;

            try { httpQueue?.Dispose(); } catch { }
            httpQueue = null;
            try { serialQueue?.Dispose(); } catch { }
            serialQueue = null;

            try { httpClient?.Dispose(); } catch { }
            httpClient = null;
        }

        // ------------------------------------------------------------------
        // Hello handshake
        // ------------------------------------------------------------------

        private void StartHelloHeartbeat()
        {
            if (httpClient == null)
            {
                helloAcknowledged = true;
                return;
            }

            helloTimer = new Timer(_ => SendHello(), null, 0, HelloIntervalMs);
        }

        private async void SendHello()
        {
            if (helloAcknowledged || disposed || httpClient == null) return;

            HelloMessage msg;
            try { msg = BuildHello(); }
            catch { return; }

            try
            {
                string json = JsonConvert.SerializeObject(msg, serializerSettings);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var cts = new CancellationTokenSource(HttpTimeoutMs))
                {
                    var response = await httpClient.PutAsync(url, content, cts.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        helloAcknowledged = true;
                        try { helloTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                        try { helloTimer?.Dispose(); } catch { }
                        helloTimer = null;
                        if (!helloLogged)
                        {
                            helloLogged = true;
                            Logger.HTTP.Log(this, "ExtensionNotifier handshake complete: " + url);
                        }
                    }
                }
            }
            catch
            {
                // Heartbeat phase: TCP refused / DNS / timeout are silently swallowed
                // per spec §3.2 "no log noise". Only the successful handshake is logged.
            }
        }

        private HelloMessage BuildHello()
        {
            string workingDir = NormalizeDir(Directory.GetCurrentDirectory());
            string baseDir = NormalizeDir(AppDomain.CurrentDomain.BaseDirectory ?? workingDir);
            string eventsDirRaw = string.IsNullOrEmpty(eventStorageLocation) ? "events" : eventStorageLocation;
            string eventsDir = NormalizeDir(Path.IsPathRooted(eventsDirRaw)
                ? eventsDirRaw
                : Path.Combine(workingDir, eventsDirRaw));
            string profileDir;
            string profileName = "default";
            try
            {
                profileDir = NormalizeDir(profile?.GetPath() ?? Path.Combine(workingDir, "data", profileName));
                if (profile != null && !string.IsNullOrEmpty(profile.Name)) profileName = profile.Name;
            }
            catch
            {
                profileDir = NormalizeDir(Path.Combine(workingDir, "data", profileName));
            }
            string pilotsDir = NormalizeDir(Path.Combine(workingDir, "pilots"));

            string version = "0.0.0.0";
            try
            {
                Assembly entry = Assembly.GetEntryAssembly();
                if (entry != null)
                {
                    Version v = entry.GetName().Version;
                    if (v != null) version = v.ToString();
                }
            }
            catch { }

            return new HelloMessage
            {
                Type = "Hello",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                FpvtVersion = version,
                Platform = DetectPlatform(),
                Paths = new HelloPaths
                {
                    WorkingDirectory = workingDir,
                    BaseDirectory = baseDir,
                    EventsDirectory = eventsDir,
                    ProfileDirectory = profileDir,
                    PilotsDirectory = pilotsDir
                },
                Profile = new HelloProfile
                {
                    Name = profileName
                },
                DecimalPlaces = decimalPlaces,
                TimingSystem = BuildTimingSystemInfo(),
                EventSettings = BuildEventSettings(),
                ChannelSettings = BuildChannelSettings()
            };
        }

        private HelloChannelSettings BuildChannelSettings()
        {
            // Event-level "Channel Settings" — the channels defined for this event,
            // independent of which pilots are assigned. Receivers use this for an
            // un-pilot-bound channel listing (e.g. spectator OSD).
            HelloChannelSettings s = new HelloChannelSettings { Channels = new List<ChannelInfo>() };
            try
            {
                Event ev = eventManager?.Event;
                if (ev?.Channels != null)
                {
                    foreach (Channel ch in ev.Channels)
                    {
                        if (ch == null) continue;
                        Color color;
                        try { color = eventManager.GetChannelColor(ch); }
                        catch { color = Color.White; }
                        s.Channels.Add(BuildChannel(ch, color));
                    }
                }
            }
            catch { }
            return s;
        }

        private HelloEventSettings BuildEventSettings()
        {
            // Event-level race rules that affect detection interpretation. Sent on Hello
            // so the Extension can render lap times consistently without re-reading XML.
            HelloEventSettings s = new HelloEventSettings();
            try
            {
                Event ev = eventManager?.Event;
                if (ev != null)
                {
                    s.RaceStartIgnoreDetections = ev.RaceStartIgnoreDetections.TotalSeconds;
                    s.MinLapTime = ev.MinLapTime.TotalSeconds;
                    s.PrimaryTimingSystemLocation = ev.PrimaryTimingSystemLocation.ToString();
                }
            }
            catch { }
            return s;
        }

        private HelloTimingSystem BuildTimingSystemInfo()
        {
            HelloTimingSystem info = new HelloTimingSystem
            {
                Count = 0,
                PrimeCount = 0,
                SplitCount = 0,
                SplitsPerLap = 0,
                AllDummy = false,
                Systems = new List<HelloTimingSystemEntry>()
            };
            try
            {
                var tsm = eventManager?.RaceManager?.TimingSystemManager;
                if (tsm == null) return info;

                info.Count = tsm.TimingSystemCount;
                info.PrimeCount = tsm.PrimeSystems?.Length ?? 0;
                info.SplitCount = tsm.SplitSystems?.Length ?? 0;
                info.SplitsPerLap = tsm.SplitsPerLap;

                var all = tsm.TimingSystems?.ToArray() ?? new ITimingSystem[0];
                info.AllDummy = all.Length > 0 && all.All(t => t is DummyTimingSystem);

                // Enumerate Prime systems first, then Splits, so that the indices
                // emitted here match TimingSystemManager.GetIndex (0 = Primary,
                // 1, 2, ... = Secondary). This is the same numbering used by
                // Detection.TimingSystemIndex, so receivers can directly look up
                // the role of a detection's gate via systems[detection.timingSystemIndex].
                IEnumerable<ITimingSystem> primeFirst =
                    (tsm.PrimeSystems ?? new ITimingSystem[0])
                    .Concat(tsm.SplitSystems ?? new ITimingSystem[0]);

                foreach (var ts in primeFirst)
                {
                    bool isSplit = tsm.SplitSystems != null && tsm.SplitSystems.Contains(ts);
                    info.Systems.Add(new HelloTimingSystemEntry
                    {
                        Index = tsm.GetIndex(ts),
                        Type = ts.GetType().Name,
                        Role = isSplit ? "Split" : "Prime"
                    });
                }
            }
            catch { }
            return info;
        }

        private static string DetectPlatform()
        {
            if (OperatingSystem.IsWindows()) return "Windows";
            if (OperatingSystem.IsMacOS()) return "macOS";
            if (OperatingSystem.IsLinux()) return "Linux";
            return Environment.OSVersion.Platform.ToString();
        }

        private static string NormalizeDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            try
            {
                string full = Path.GetFullPath(path);
                if (!full.EndsWith(Path.DirectorySeparatorChar))
                    full += Path.DirectorySeparatorChar;
                return full;
            }
            catch
            {
                return path;
            }
        }

        // ------------------------------------------------------------------
        // Race lifecycle
        // ------------------------------------------------------------------

        private void RaceManager_OnRaceStartScheduled(Race race, DateTime startTime)
        {
            // We emit RacePreStart from OnRaceStartScheduled (not OnRacePreStart) so
            // that scheduledStart carries the *post-randomisation* planned start
            // moment — the exact instant RaceManager will hand off to OnRaceStart.
            // OnRacePreStart fires earlier (inside PreRaceStart()) before the random
            // delay between MinStartDelay and MaxStartDelay has been picked, so its
            // timestamp would only be an upper-bound estimate. With random delays
            // (Min != Max), receivers anchoring LED/strobe start cues to scheduledStart
            // need the resolved value, not the worst case.
            if (race == null) return;
            Send(new RaceLifecycleMessage
            {
                Type = "RacePreStart",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString(),
                ScheduledStart = ToUtcIso(startTime)
            });
        }

        private void RaceManager_OnRaceChanged(Race race)
        {
            if (race == null) return;

            Send(BuildRaceLoaded(race));

            Race next;
            try
            {
                next = eventManager.RaceManager.GetNextRace(true, allowCurrent: false);
            }
            catch
            {
                next = null;
            }
            Send(BuildNextRace(next));
        }

        // Fires the instant SoundManager.StartRaceIn() begins speaking the
        // "Arm your quads…" announcement — i.e. EventLayer.StartRace's
        // delayedStart branch. Distinct from RacePreStart, which is sent
        // ~MaxStartDelay seconds later when the speech callback runs
        // RaceManager.StartRace → StartRaceInLessThan → OnRaceStartScheduled.
        // Receivers anchor LED/strobe cues at speech-start (well before the
        // randomised PreRaceStart resolution) by listening for this event.
        public void EmitRaceStartAnnouncement(TimeSpan delay, Race race)
        {
            if (disposed || race == null) return;
            DateTime nowUtc = DateTime.UtcNow;
            Send(new RaceStartAnnouncementMessage
            {
                Type = "RaceStartAnnouncement",
                Ts = nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString(),
                DelaySeconds = delay.TotalSeconds,
                ExpectedStart = (nowUtc + delay).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            });
        }

        private void RaceManager_OnRaceStart(Race race)
        {
            if (race == null) return;
            Send(new RaceLifecycleMessage
            {
                Type = "RaceStart",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString(),
                ActualStart = ToUtcIso(race.Start)
            });
        }

        private void RaceManager_OnRaceEnd(Race race)
        {
            if (race == null) return;
            Send(new RaceLifecycleMessage
            {
                Type = "RaceEnd",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString()
            });

            // RaceResult is emitted from ResultManager.RaceResultsChanged because
            // ResultManager.SaveResults runs after the race ends and populates results.
            // If for some reason that does not fire, emit a best-effort result here too.
        }

        private void RaceManager_OnRaceCancelled(Race race, bool failure)
        {
            if (race == null) return;
            Send(new RaceLifecycleMessage
            {
                Type = "RaceCancelled",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString(),
                Failure = failure
            });
        }

        private void RaceManager_OnRaceTimesUp(Race race)
        {
            if (race == null) return;
            Send(new RaceLifecycleMessage
            {
                Type = "RaceTimesUp",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString()
            });
        }

        private void RaceManager_OnPilotsChanged(PilotChannel pc)
        {
            Race race = eventManager.RaceManager.CurrentRace;
            if (race == null) return;

            Send(new PilotRaceStateMessage
            {
                Type = "PilotRaceState",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString(),
                Pilots = BuildPilots(race)
            });
        }

        private void RaceManager_OnChannelCrashedOut(Channel channel, Pilot pilot, bool manual)
        {
            if (pilot == null) return;
            Race race = eventManager.RaceManager.CurrentRace;
            Color color = race != null
                ? eventManager.GetRaceChannelColor(race, channel)
                : eventManager.GetChannelColor(channel);

            Send(new PilotCrashedOutMessage
            {
                Type = "PilotCrashedOut",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Pilot = BuildPilotInfo(pilot, channel, color),
                ManuallySet = manual
            });
        }

        // Fires once per pilot during TimeTrial staggered start, on the same
        // worker thread that drives RaceManager.StartStaggered. Receivers light
        // the pilot's lane / announce "<pilot> START" at this exact moment.
        private void RaceManager_OnPilotStartStaggered(Race race, PilotChannel pc, int orderIndex, int totalPilots, TimeSpan delay)
        {
            if (race == null || pc?.Pilot == null || pc.Channel == null) return;
            Color color = eventManager.GetRaceChannelColor(race, pc.Channel);
            Send(new PilotStaggeredStartMessage
            {
                Type = "PilotStaggeredStart",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                Pilot = BuildPilotInfo(pc.Pilot, pc.Channel, color),
                OrderIndex = orderIndex,
                TotalPilots = totalPilots,
                DelaySeconds = delay.TotalSeconds
            });
        }

        // ------------------------------------------------------------------
        // Detection
        // ------------------------------------------------------------------

        private void RaceManager_OnDetection(Detection det)
        {
            EmitDetection(det);
        }

        private void RaceManager_OnLapDetected(Lap lap)
        {
            if (lap?.Detection == null) return;
            EmitDetection(lap.Detection);
        }

        private void EmitDetection(Detection det)
        {
            if (det == null || det.Pilot == null) return;

            Race race = eventManager.RaceManager.CurrentRace;
            if (race == null) return;

            Guid id = det.ID;
            if (id != Guid.Empty)
            {
                lock (detectionLock)
                {
                    if (recentDetectionIds.Contains(id)) return;
                    recentDetectionIds.Add(id);
                    recentDetectionOrder.Enqueue(id);
                    while (recentDetectionOrder.Count > RecentDetectionIdsCap)
                    {
                        Guid old = recentDetectionOrder.Dequeue();
                        recentDetectionIds.Remove(old);
                    }
                }
            }

            Color color = eventManager.GetRaceChannelColor(race, det.Channel);
            int sectorCount = (eventManager.Event?.Sectors?.Length ?? 0);
            int sectorIndex;
            if (sectorCount > 0)
            {
                sectorIndex = (det.TimingSystemIndex % sectorCount) + 1;
            }
            else
            {
                // No sectors configured. Per spec §7.4 sectorIndex is 1-based and on
                // IsLapEnd it represents the final sector of the lap — clamp to >= 1.
                sectorIndex = Math.Max(1, det.TimingSystemIndex + 1);
            }

            TimeSpan raceTime = race.Start != default(DateTime) ? det.Time - race.Start : TimeSpan.Zero;
            double? sectorSeconds = TryComputeSectorTime(race, det);
            double lapTimeSoFar = TryComputeLapTimeSoFar(race, det);
            int position = race.GetTrackPosition(det.Pilot);

            bool finished = false;
            try { finished = det.Pilot.HasFinished(eventManager, race); } catch { }

            DetectionExtMessage msg = new DetectionExtMessage
            {
                Type = "DetectionExt",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                DetectionId = id.ToString(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                PilotName = det.Pilot.Name,
                Channel = BuildChannel(det.Channel, color),
                TimingSystemIndex = det.TimingSystemIndex,
                IsLapEnd = det.IsLapEnd,
                LapNumber = det.LapNumber,
                SectorIndex = sectorIndex,
                RaceSector = det.RaceSector,
                RaceTime = raceTime.TotalSeconds,
                SectorTime = sectorSeconds,
                LapTimeSoFar = lapTimeSoFar,
                Position = position,
                Valid = det.Valid,
                PositionSnapshot = BuildPositionSnapshot(race),
                RaceFinishedForPilot = finished
            };
            Send(msg);
        }

        private static double? TryComputeSectorTime(Race race, Detection det)
        {
            try
            {
                Detection prev;
                lock (race.Detections)
                {
                    prev = race.Detections
                        .Where(d => d != det && d.Pilot == det.Pilot && d.Time < det.Time && d.Valid)
                        .OrderByDescending(d => d.Time)
                        .FirstOrDefault();
                }
                if (prev == null) return null;
                return (det.Time - prev.Time).TotalSeconds;
            }
            catch
            {
                return null;
            }
        }

        private static double TryComputeLapTimeSoFar(Race race, Detection det)
        {
            try
            {
                if (race.Start == default(DateTime)) return 0;

                Detection lapStart;
                lock (race.Detections)
                {
                    lapStart = race.Detections
                        .Where(d => d != det && d.Pilot == det.Pilot && d.IsLapEnd && d.Time < det.Time && d.Valid)
                        .OrderByDescending(d => d.Time)
                        .FirstOrDefault();
                }
                DateTime from = lapStart?.Time ?? race.Start;
                return (det.Time - from).TotalSeconds;
            }
            catch
            {
                return 0;
            }
        }

        private List<PositionEntry> BuildPositionSnapshot(Race race)
        {
            List<PositionEntry> result = new List<PositionEntry>();
            try
            {
                Pilot[] pilots;
                lock (race.PilotChannels)
                {
                    pilots = race.PilotChannels
                        .Where(pc => pc.Pilot != null)
                        .Select(pc => pc.Pilot)
                        .ToArray();
                }

                foreach (Pilot p in pilots)
                {
                    int pos = race.GetTrackPosition(p);
                    Detection last;
                    lock (race.Detections)
                    {
                        last = race.Detections
                            .Where(d => d.Valid && d.Pilot == p)
                            .OrderByDescending(d => d.Time)
                            .FirstOrDefault();
                    }
                    int raceSector = last?.RaceSector ?? 0;
                    double lastSec = (last != null && race.Start != default(DateTime))
                        ? (last.Time - race.Start).TotalSeconds
                        : 0;
                    result.Add(new PositionEntry
                    {
                        PilotName = p.Name,
                        Position = pos,
                        RaceSector = raceSector,
                        LastDetectionTime = lastSec
                    });
                }
                result = result.OrderBy(e => e.Position).ToList();
            }
            catch { }
            return result;
        }

        // ------------------------------------------------------------------
        // ResultManager → RaceResult + StageRanking
        // ------------------------------------------------------------------

        private void ResultManager_OnRaceResultsChanged(Race race)
        {
            if (race == null) return;

            Send(BuildRaceResult(race));

            Stage stage = race.Round?.Stage;
            if (stage != null)
            {
                Send(BuildStageRanking(stage));
            }
        }

        // ------------------------------------------------------------------
        // Builders
        // ------------------------------------------------------------------

        private RaceLoadedMessage BuildRaceLoaded(Race race)
        {
            return new RaceLoadedMessage
            {
                Type = "RaceLoaded",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString(),
                ScheduledStart = race.ScheduledStart != default(DateTime)
                    ? (string)ToUtcIso(race.ScheduledStart)
                    : null,
                TargetLaps = race.TargetLaps,
                RaceLength = (eventManager.Event?.RaceLength ?? TimeSpan.Zero).TotalSeconds,
                Stage = BuildStage(race.Round?.Stage),
                Sectors = BuildSectors(),
                Pilots = BuildPilots(race)
            };
        }

        private NextRaceMessage BuildNextRace(Race next)
        {
            if (next == null)
            {
                return new NextRaceMessage
                {
                    Type = "NextRace",
                    Ts = NowUtcIso(),
                    Seq = NextSeq(),
                    Round = null,
                    Race = null,
                    RaceType = null,
                    ScheduledStart = null,
                    Pilots = new List<PilotInfoExt>()
                };
            }
            return new NextRaceMessage
            {
                Type = "NextRace",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = next.RoundNumber,
                Race = next.RaceNumber,
                RaceType = next.Type.ToString(),
                ScheduledStart = next.ScheduledStart != default(DateTime)
                    ? (string)ToUtcIso(next.ScheduledStart)
                    : null,
                Pilots = BuildPilots(next)
            };
        }

        private RaceResultMessage BuildRaceResult(Race race)
        {
            List<PilotResultEntry> pilots = new List<PilotResultEntry>();
            try
            {
                Result[] results = eventManager.ResultManager.GetOrderedResults(race).ToArray();

                foreach (Result r in results)
                {
                    if (r.Pilot == null) continue;
                    Channel channel = race.GetChannel(r.Pilot);
                    Color color = channel != null ? eventManager.GetRaceChannelColor(race, channel) : Color.White;

                    // GetBestLapsTime returns TimeSpan.MaxValue when the pilot doesn't
                    // have enough valid laps for the requested count — treat that as null.
                    double? bestLap = TryBestLap(race, r.Pilot, 1);

                    int consecCount = race.TargetLaps > 0 ? Math.Min(3, race.TargetLaps) : 3;
                    double? bestConsec = TryBestLap(race, r.Pilot, consecCount);

                    pilots.Add(new PilotResultEntry
                    {
                        Pilot = BuildPilotInfo(r.Pilot, channel, color),
                        Position = r.Position,
                        TotalLaps = r.LapsFinished,
                        TotalTime = r.Time.TotalSeconds,
                        BestLap = bestLap,
                        BestConsecutive = bestConsec.HasValue
                            ? new BestConsecutiveEntry { Laps = consecCount, Time = bestConsec.Value }
                            : null,
                        DNF = r.DNF
                    });
                }
            }
            catch { }

            return new RaceResultMessage
            {
                Type = "RaceResult",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Round = race.RoundNumber,
                Race = race.RaceNumber,
                RaceType = race.Type.ToString(),
                Pilots = pilots
            };
        }

        private StageRankingMessage BuildStageRanking(Stage stage)
        {
            List<StageRankingEntry> ranking = new List<StageRankingEntry>();
            try
            {
                Race[] stageRaces = eventManager.RaceManager.GetRaces(r => r.Round != null && r.Round.Stage == stage && r.Valid).ToArray();
                Pilot[] pilots = stageRaces.SelectMany(r => r.Pilots).Where(p => p != null).Distinct().ToArray();
                Round endRound = stageRaces.OrderByDescending(r => r.RoundNumber).FirstOrDefault()?.Round;

                List<Tuple<Pilot, int, double, double>> rows = new List<Tuple<Pilot, int, double, double>>();
                foreach (Pilot p in pilots)
                {
                    int points = 0;
                    try
                    {
                        if (endRound != null) points = eventManager.ResultManager.GetPointsTotal(endRound, p);
                    }
                    catch { }

                    double bestLap = 0;
                    double bestConsec = 0;
                    foreach (Race r in stageRaces)
                    {
                        double? bl = TryBestLap(r, p, 1);
                        if (bl.HasValue && (bestLap == 0 || bl.Value < bestLap)) bestLap = bl.Value;

                        int cc = r.TargetLaps > 0 ? Math.Min(3, r.TargetLaps) : 3;
                        double? bc = TryBestLap(r, p, cc);
                        if (bc.HasValue && (bestConsec == 0 || bc.Value < bestConsec)) bestConsec = bc.Value;
                    }
                    rows.Add(Tuple.Create(p, points, bestLap, bestConsec));
                }

                int pos = 1;
                int prevPoints = int.MinValue;
                int rank = 0;
                foreach (var row in rows.OrderByDescending(r => r.Item2))
                {
                    rank++;
                    int p = row.Item2 != prevPoints ? rank : pos;
                    pos = p;
                    prevPoints = row.Item2;

                    Race anyRace = stageRaces.FirstOrDefault(r => r.HasPilot(row.Item1));
                    Channel ch = anyRace?.GetChannel(row.Item1);
                    Color color = (anyRace != null && ch != null)
                        ? eventManager.GetRaceChannelColor(anyRace, ch)
                        : Color.White;

                    int consecCount = (anyRace != null && anyRace.TargetLaps > 0) ? Math.Min(3, anyRace.TargetLaps) : 3;

                    ranking.Add(new StageRankingEntry
                    {
                        Pilot = BuildPilotInfo(row.Item1, ch, color),
                        Position = p,
                        Points = row.Item2,
                        BestLap = row.Item3 > 0 ? (double?)row.Item3 : null,
                        BestConsecutive = row.Item4 > 0
                            ? new BestConsecutiveEntry { Laps = consecCount, Time = row.Item4 }
                            : null
                    });
                }
            }
            catch { }

            return new StageRankingMessage
            {
                Type = "StageRanking",
                Ts = NowUtcIso(),
                Seq = NextSeq(),
                Stage = BuildStage(stage),
                Ranking = ranking
            };
        }

        private List<PilotInfoExt> BuildPilots(Race race)
        {
            List<PilotInfoExt> list = new List<PilotInfoExt>();
            if (race == null) return list;

            PilotChannel[] pcs;
            lock (race.PilotChannels)
            {
                pcs = race.PilotChannels.Where(pc => pc.Pilot != null && pc.Channel != null).ToArray();
            }

            foreach (PilotChannel pc in pcs)
            {
                Color color = eventManager.GetRaceChannelColor(race, pc.Channel);
                list.Add(BuildPilotInfo(pc.Pilot, pc.Channel, color));
            }
            return list;
        }

        private static PilotInfoExt BuildPilotInfo(Pilot p, Channel c, Color color)
        {
            return new PilotInfoExt
            {
                Name = p?.Name,
                Phonetic = p?.Phonetic,
                DiscordID = p?.DiscordID,
                PhotoPath = p?.PhotoPath,
                VideoFlipped = p != null && p.VideoFlipped,
                VideoMirrored = p != null && p.VideoMirrored,
                Channel = c != null ? BuildChannel(c, color) : null
            };
        }

        private static ChannelInfo BuildChannel(Channel c, Color color)
        {
            return new ChannelInfo
            {
                Band = c.Band.ToString(),
                Number = c.Number,
                Frequency = c.Frequency,
                ColorR = color.R,
                ColorG = color.G,
                ColorB = color.B
            };
        }

        private List<SectorInfoEntry> BuildSectors()
        {
            List<SectorInfoEntry> list = new List<SectorInfoEntry>();
            try
            {
                Sector[] sectors = eventManager.Event?.Sectors;
                if (sectors == null) return list;
                foreach (Sector s in sectors)
                {
                    list.Add(new SectorInfoEntry
                    {
                        Number = s.Number,
                        Length = s.Length,
                        CalculateSpeed = s.CalculateSpeed
                    });
                }
            }
            catch { }
            return list;
        }

        private static StageInfoEntry BuildStage(Stage stage)
        {
            if (stage == null) return null;
            return new StageInfoEntry
            {
                Name = stage.Name,
                StageType = stage.StageType.ToString()
            };
        }

        // ------------------------------------------------------------------
        // Send
        // ------------------------------------------------------------------

        private void Send(object payload)
        {
            if (disposed || payload == null) return;

            long traceSeq = TryReadSeq(payload);
            string traceType = TryReadType(payload) ?? payload.GetType().Name;

            string json;
            try
            {
                json = JsonConvert.SerializeObject(payload, serializerSettings);
            }
            catch (Exception ex)
            {
                Logger.HTTP.Log(this, "ExtSeq enter+serialize-failed seq=" + traceSeq + " type=" + traceType);
                Logger.HTTP.LogException(this, ex);
                return;
            }

            Logger.HTTP.Log(this, "ExtSeq enter seq=" + traceSeq + " type=" + traceType + " bytes=" + json.Length);

            EnqueueHttp(json, traceSeq, traceType);
            EnqueueSerial(json, traceSeq, traceType);
        }

        private static long TryReadSeq(object payload)
        {
            try
            {
                var p = payload.GetType().GetProperty("Seq");
                if (p == null) return -1;
                object v = p.GetValue(payload);
                return v is long lv ? lv : (v is int iv ? (long)iv : -1);
            }
            catch { return -1; }
        }

        private static string TryReadType(object payload)
        {
            try
            {
                var p = payload.GetType().GetProperty("Type");
                if (p == null) return null;
                return p.GetValue(payload) as string;
            }
            catch { return null; }
        }

        private void EnqueueHttp(string json, long traceSeq, string traceType)
        {
            if (httpClient == null || string.IsNullOrEmpty(url))
            {
                Logger.HTTP.Log(this, "ExtSeq http-skip seq=" + traceSeq + " type=" + traceType + " (no http client)");
                return;
            }

            int size = Interlocked.Increment(ref httpQueueSize);
            if (size > HttpQueueCapacity)
            {
                Interlocked.Decrement(ref httpQueueSize);
                Logger.HTTP.Log(this, "ExtSeq http-drop-queue-full seq=" + traceSeq + " type=" + traceType + " size=" + size);
                return;
            }

            httpQueue.Enqueue(() =>
            {
                bool ok = false;
                int status = -1;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var cts = new CancellationTokenSource(HttpTimeoutMs))
                    {
                        var task = httpClient.PutAsync(url, content, cts.Token);
                        task.Wait();
                        var resp = task.Result;
                        status = (int)resp.StatusCode;
                        ok = resp.IsSuccessStatusCode;
                    }
                }
                catch (Exception e)
                {
                    Logger.HTTP.Log(this, "ExtSeq http-fail seq=" + traceSeq + " type=" + traceType + " elapsedMs=" + sw.ElapsedMilliseconds + " ex=" + e.GetType().Name + ":" + e.Message);
                    if (lastHttpExceptionType == null || lastHttpExceptionType != e.GetType())
                    {
                        Logger.HTTP.LogException(this, e);
                        lastHttpExceptionType = e.GetType();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref httpQueueSize);
                }

                if (ok)
                {
                    Logger.HTTP.Log(this, "ExtSeq http-ok seq=" + traceSeq + " type=" + traceType + " elapsedMs=" + sw.ElapsedMilliseconds + " status=" + status);
                }
                else if (status > 0)
                {
                    Logger.HTTP.Log(this, "ExtSeq http-non2xx seq=" + traceSeq + " type=" + traceType + " elapsedMs=" + sw.ElapsedMilliseconds + " status=" + status);
                }
            });
        }

        private void EnqueueSerial(string json, long traceSeq, string traceType)
        {
            if (serialPort == null) return;

            int size = Interlocked.Increment(ref serialQueueSize);
            if (size > SerialQueueCapacity)
            {
                Interlocked.Decrement(ref serialQueueSize);
                Logger.HTTP.Log(this, "ExtSeq serial-drop-queue-full seq=" + traceSeq + " type=" + traceType);
                return;
            }

            serialQueue.Enqueue(() =>
            {
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    serialPort?.Write(bytes, 0, bytes.Length);
                }
                catch (Exception e)
                {
                    if (lastSerialExceptionType == null || lastSerialExceptionType != e.GetType())
                    {
                        Logger.HTTP.LogException(this, e);
                        lastSerialExceptionType = e.GetType();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref serialQueueSize);
                }
            });
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private long NextSeq() => Interlocked.Increment(ref seqCounter);

        // GetBestLapsTime returns TimeSpan.MaxValue when there are not enough valid laps.
        // We surface that as null in the wire protocol rather than a 9.2e11-second value.
        private static double? TryBestLap(Race race, Pilot pilot, int consecutiveLaps)
        {
            try
            {
                TimeSpan t = race.GetBestLapsTime(pilot, consecutiveLaps);
                if (t == TimeSpan.MaxValue || t <= TimeSpan.Zero) return null;
                return t.TotalSeconds;
            }
            catch
            {
                return null;
            }
        }

        private static string NowUtcIso() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        private static string ToUtcIso(DateTime dt)
        {
            if (dt == default(DateTime)) return null;
            DateTime utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
    }

    // ----------------------------------------------------------------------
    // Payload DTOs
    // ----------------------------------------------------------------------

    public class HelloMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public string FpvtVersion { get; set; }
        public string Platform { get; set; }
        public HelloPaths Paths { get; set; }
        public HelloProfile Profile { get; set; }
        public int DecimalPlaces { get; set; }
        public HelloTimingSystem TimingSystem { get; set; }
        public HelloEventSettings EventSettings { get; set; }
        public HelloChannelSettings ChannelSettings { get; set; }
    }

    public class HelloChannelSettings
    {
        // Event-level channel roster ("Channel Settings"). Each entry is a ChannelInfo
        // (§6.1) with the event-assigned display color.
        public List<ChannelInfo> Channels { get; set; }
    }

    public class HelloEventSettings
    {
        // Seconds after race start during which detections are ignored (Event setting:
        // "Race Start Ignore Detections"). Lets the Extension match FPVTrackside's
        // detection-validity logic when rendering live timing.
        public double RaceStartIgnoreDetections { get; set; }

        // Smart minimum lap time in seconds (Event setting: "Smart Minimum Lap Time").
        // Detections producing a lap faster than this are filtered as duplicates.
        public double MinLapTime { get; set; }

        // "Holeshot" = goal gate sits at the start line, so the first crossing is the
        // holeshot (lap 0 -> lap 1). "EndOfLap" = goal gate is past the start, so the
        // first crossing IS lap 1 end (no holeshot crossing exists).
        public string PrimaryTimingSystemLocation { get; set; }
    }

    public class HelloTimingSystem
    {
        public int Count { get; set; }
        public int PrimeCount { get; set; }
        public int SplitCount { get; set; }
        public int SplitsPerLap { get; set; }
        public bool AllDummy { get; set; }
        public List<HelloTimingSystemEntry> Systems { get; set; }
    }

    public class HelloTimingSystemEntry
    {
        public int Index { get; set; }
        public string Type { get; set; }
        public string Role { get; set; }
    }

    public class HelloPaths
    {
        public string WorkingDirectory { get; set; }
        public string BaseDirectory { get; set; }
        public string EventsDirectory { get; set; }
        public string ProfileDirectory { get; set; }
        public string PilotsDirectory { get; set; }
    }

    public class HelloProfile
    {
        public string Name { get; set; }
    }

    public class RaceLifecycleMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public int Round { get; set; }
        public int Race { get; set; }
        public string RaceType { get; set; }
        public string ActualStart { get; set; }
        public string ScheduledStart { get; set; }
        public bool? Failure { get; set; }

        public bool ShouldSerializeActualStart() => ActualStart != null;
        public bool ShouldSerializeScheduledStart() => ScheduledStart != null;
        public bool ShouldSerializeFailure() => Failure.HasValue;
    }

    public class RaceLoadedMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public int Round { get; set; }
        public int Race { get; set; }
        public string RaceType { get; set; }
        public string ScheduledStart { get; set; }
        public int TargetLaps { get; set; }
        public double RaceLength { get; set; }
        public StageInfoEntry Stage { get; set; }
        public List<SectorInfoEntry> Sectors { get; set; }
        public List<PilotInfoExt> Pilots { get; set; }
    }

    public class NextRaceMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public int? Round { get; set; }
        public int? Race { get; set; }
        public string RaceType { get; set; }
        public string ScheduledStart { get; set; }
        public List<PilotInfoExt> Pilots { get; set; }
    }

    public class DetectionExtMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public string DetectionId { get; set; }
        public int Round { get; set; }
        public int Race { get; set; }
        public string PilotName { get; set; }
        public ChannelInfo Channel { get; set; }
        public int TimingSystemIndex { get; set; }
        public bool IsLapEnd { get; set; }
        public int LapNumber { get; set; }
        public int SectorIndex { get; set; }
        public int RaceSector { get; set; }
        public double RaceTime { get; set; }
        public double? SectorTime { get; set; }
        public double LapTimeSoFar { get; set; }
        public int Position { get; set; }
        public bool Valid { get; set; }
        public List<PositionEntry> PositionSnapshot { get; set; }
        public bool RaceFinishedForPilot { get; set; }
    }

    public class RaceResultMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public int Round { get; set; }
        public int Race { get; set; }
        public string RaceType { get; set; }
        public List<PilotResultEntry> Pilots { get; set; }
    }

    public class StageRankingMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public StageInfoEntry Stage { get; set; }
        public List<StageRankingEntry> Ranking { get; set; }
    }

    public class PilotRaceStateMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public int Round { get; set; }
        public int Race { get; set; }
        public string RaceType { get; set; }
        public List<PilotInfoExt> Pilots { get; set; }
    }

    public class PilotCrashedOutMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public PilotInfoExt Pilot { get; set; }
        public bool ManuallySet { get; set; }
    }

    public class RaceStartAnnouncementMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public int Round { get; set; }
        public int Race { get; set; }
        public string RaceType { get; set; }
        public double DelaySeconds { get; set; }
        public string ExpectedStart { get; set; }
    }

    public class PilotStaggeredStartMessage
    {
        public string Type { get; set; }
        public string Ts { get; set; }
        public long Seq { get; set; }
        public int Round { get; set; }
        public int Race { get; set; }
        public PilotInfoExt Pilot { get; set; }
        public int OrderIndex { get; set; }
        public int TotalPilots { get; set; }
        public double DelaySeconds { get; set; }
    }

    public class PilotInfoExt
    {
        public string Name { get; set; }
        public string Phonetic { get; set; }
        public string DiscordID { get; set; }
        public string PhotoPath { get; set; }
        public bool VideoFlipped { get; set; }
        public bool VideoMirrored { get; set; }
        public ChannelInfo Channel { get; set; }
    }

    public class ChannelInfo
    {
        public string Band { get; set; }
        public int Number { get; set; }
        public int Frequency { get; set; }
        public byte ColorR { get; set; }
        public byte ColorG { get; set; }
        public byte ColorB { get; set; }
    }

    public class PositionEntry
    {
        public string PilotName { get; set; }
        public int Position { get; set; }
        public int RaceSector { get; set; }
        public double LastDetectionTime { get; set; }
    }

    public class StageInfoEntry
    {
        public string Name { get; set; }
        public string StageType { get; set; }
    }

    public class SectorInfoEntry
    {
        public int Number { get; set; }
        public double Length { get; set; }
        public bool CalculateSpeed { get; set; }
    }

    public class PilotResultEntry
    {
        public PilotInfoExt Pilot { get; set; }
        public int Position { get; set; }
        public int TotalLaps { get; set; }
        public double TotalTime { get; set; }
        public double? BestLap { get; set; }
        public BestConsecutiveEntry BestConsecutive { get; set; }
        public bool DNF { get; set; }
    }

    public class StageRankingEntry
    {
        public PilotInfoExt Pilot { get; set; }
        public int Position { get; set; }
        public int? Points { get; set; }
        public double? BestLap { get; set; }
        public BestConsecutiveEntry BestConsecutive { get; set; }
    }

    public class BestConsecutiveEntry
    {
        public int Laps { get; set; }
        public double Time { get; set; }
    }
}

