using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tools;

namespace Timing.Aruco
{
    /// <summary>
    /// Records the reference (Primary) ArUco instance's per-frame marker count for each racing
    /// channel, and keeps it available to the marshal graph after the race.
    ///
    /// The graph plots an RSSI trace; ArUco has no RSSI, so it is handed the raw marker count -
    /// the very number <see cref="ArucoTimingSystem.ReportMarkerCount"/> thresholds on, so why a
    /// lap was or was not detected can be read straight off the graph. Counts are multiplied by
    /// <see cref="Scale"/> on the way out purely to give the trace and its integer threshold line
    /// some vertical room; the graph divides by the same number to label the axis in markers.
    ///
    /// Deliberately a separate class from the timing system, and touched only by ArUco code: none
    /// of this exists on the RotorHazard path, which fetches its waveform from the RotorHazard
    /// server on demand and stores nothing locally.
    /// </summary>
    internal class ArucoWaveformRecorder
    {
        /// <summary>Marker counts are published to the graph multiplied by this.</summary>
        public const int Scale = 10;

        /// <summary>
        /// Every race is written to disk, so this is only a cache - a race that has fallen out
        /// of it is re-read on demand. Two covers "open the marshal editor on the race that just
        /// finished" without holding a whole meeting in memory.
        /// </summary>
        private const int RetainedRaces = 2;

        /// <summary>
        /// One sample per video frame per channel, so this ceiling is really a duration: about
        /// 33 minutes of a 30fps capture, 16 of a 60fps one. Bounds the capture if EndDetection
        /// never arrives, and is logged when reached rather than silently dropping the rest.
        /// </summary>
        private const int MaxSamplesPerChannel = 60000;

        /// <summary>
        /// Where per-race files live - events/&lt;EventID&gt;/aruco_waveform, supplied by the UI,
        /// which is the only layer that knows it. Null until then, and null is a valid state:
        /// nothing is written or read, and the capture simply stays in memory as before.
        /// </summary>
        public string Directory { get; set; }

        private readonly List<RaceCapture> races = new List<RaceCapture>();
        private RaceCapture current;
        private readonly object captureLock = new object();

        private class ChannelSamples
        {
            public readonly List<TimeSpan> Times = new List<TimeSpan>();
            public readonly List<int> Counts = new List<int>();

            /// <summary>Set once the cap is reached, so it is logged once rather than per frame.</summary>
            public bool CapReported;
        }

        private class RaceCapture
        {
            public Guid RaceId;
            public DateTime Start;
            public Dictionary<int, Guid> PilotByFrequency;
            public readonly Dictionary<int, ChannelSamples> ByFrequency = new Dictionary<int, ChannelSamples>();

            /// <summary>
            /// MarkerThreshold as it stood when this race ran, for a capture read back from disk.
            /// Null for a live one, which reads the current setting - it cannot have changed
            /// between the race ending and the marshal editor opening on it.
            /// </summary>
            public int? StoredThreshold;
        }

        public void Start(Guid raceId, DateTime raceStart, IEnumerable<KeyValuePair<int, Guid>> pilotByFrequency)
        {
            RaceCapture race = new RaceCapture
            {
                RaceId = raceId,
                Start = raceStart,
                PilotByFrequency = pilotByFrequency.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            };

            lock (captureLock)
            {
                races.Add(race);
                Trim();
                current = race;
            }
        }

        public void Record(int frequency, int count, DateTime captureTime)
        {
            lock (captureLock)
            {
                RaceCapture race = current;
                if (race == null)
                    return;

                if (!race.ByFrequency.TryGetValue(frequency, out ChannelSamples samples))
                {
                    samples = new ChannelSamples();
                    race.ByFrequency[frequency] = samples;
                }

                if (samples.Times.Count >= MaxSamplesPerChannel)
                {
                    if (!samples.CapReported)
                    {
                        samples.CapReported = true;
                        Log(frequency + "mhz hit the " + MaxSamplesPerChannel + " sample cap at "
                            + (captureTime - race.Start).TotalSeconds.ToString("0")
                            + "s - the rest of this race is not in the graph");
                    }
                    return;
                }

                // Times are relative to the race start handed to StartDetection. That can be in
                // the future - a delayed start arms detection first - so early samples land at
                // negative times, which is exactly where a phantom first lap comes from.
                samples.Times.Add(captureTime - race.Start);
                samples.Counts.Add(count);
            }
        }

        /// <summary>Called when the race ends: report what was captured, then persist it.</summary>
        public void Finish(int markerThreshold)
        {
            LogCapture();
            Save(markerThreshold);
        }

        public RSSIWaveform Get(Guid raceId, Guid pilotId, int currentMarkerThreshold)
        {
            if (pilotId == Guid.Empty)
            {
                Log("asked for an empty pilot id");
                return null;
            }

            lock (captureLock)
            {
                RaceCapture race = races.LastOrDefault(r => r.RaceId == raceId);

                if (race == null)
                {
                    // Not in memory - a restart, a timing-settings change (which rebuilds every
                    // ITimingSystem), or simply several races ago. Fall back to the file written
                    // when that race ended, and keep it so the next pilot does not re-read it.
                    race = Load(raceId);

                    if (race != null)
                    {
                        races.Add(race);
                        Trim();
                    }
                }

                if (race == null)
                {
                    Log("no capture held for race " + raceId + " (" + races.Count
                        + " race(s) in memory, directory " + (Directory ?? "not set") + ")");
                    return null;
                }

                int frequency = 0;
                bool found = false;
                foreach (var kvp in race.PilotByFrequency)
                {
                    if (kvp.Value != pilotId) continue;
                    frequency = kvp.Key;
                    found = true;
                    break;
                }

                if (!found)
                {
                    Log("pilot " + pilotId + " has no channel in the capture for race " + raceId);
                    return null;
                }

                if (!race.ByFrequency.TryGetValue(frequency, out ChannelSamples samples) || samples.Times.Count == 0)
                {
                    Log("no samples on " + frequency + "mhz for race " + raceId);
                    return null;
                }

                // Enter and Exit are one and the same number here: the gate is in view while
                // count >= MarkerThreshold and out of view below it, and it is FlickerLengthMs -
                // not a second, lower threshold - that decides when the crossing is emitted.
                int threshold = (race.StoredThreshold ?? currentMarkerThreshold) * Scale;

                return new ArucoWaveform
                {
                    Times = samples.Times.ToArray(),
                    Values = samples.Counts.Select(c => c * Scale).ToArray(),
                    EnterAt = threshold,
                    ExitAt = threshold,
                    MarkerThreshold = threshold / Scale,
                    ValueScale = Scale,
                    ValueLabel = "markers",
                };
            }
        }

        public void Clear()
        {
            lock (captureLock)
            {
                races.Clear();
                current = null;
            }
        }

        /// <summary>
        /// Throw away everything held for one race, memory and file alike.
        ///
        /// Resetting a race keeps its id and clears its laps, so without this the next marshal
        /// editor opened on it would plot the previous run's trace behind an empty lap list -
        /// the graph would look like a race that never happened had one.
        /// </summary>
        public void Discard(Guid raceId)
        {
            bool held;

            lock (captureLock)
            {
                held = races.RemoveAll(r => r.RaceId == raceId) > 0;

                if (current != null && current.RaceId == raceId)
                    current = null;
            }

            string path = PathFor(raceId);

            try
            {
                if (path != null && File.Exists(path))
                {
                    File.Delete(path);
                    held = true;
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog?.LogException(this, ex);
                Log("could not delete " + path);
            }

            if (held)
                Log("discarded race " + raceId);
        }

        /// <summary>Caller holds captureLock.</summary>
        private void Trim()
        {
            while (races.Count > RetainedRaces)
            {
                races.RemoveAt(0);
            }
        }

        private void LogCapture()
        {
            lock (captureLock)
            {
                RaceCapture race = current;
                if (race == null)
                {
                    Log("nothing captured (no race started)");
                    return;
                }

                string channels = string.Join(", ", race.ByFrequency
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => kvp.Key + "mhz=" + kvp.Value.Times.Count
                                   + (race.PilotByFrequency.ContainsKey(kvp.Key) ? "" : " (no pilot)")));

                Log("captured for race " + race.RaceId + ": " + race.ByFrequency.Count
                    + " channel(s) [" + channels + "], " + race.PilotByFrequency.Count + " pilot(s) mapped");
            }
        }

        // --- persistence ---------------------------------------------------------------------

        /// <summary>
        /// On-disk form of one race. Counts are stored unscaled - the x10 is a graph concern, so
        /// a file written by one build still reads correctly if Scale ever changes. Times are
        /// milliseconds from the race start, and may be negative.
        /// </summary>
        private class StoredWaveform
        {
            public Guid RaceId { get; set; }
            public int MarkerThreshold { get; set; }
            public Dictionary<string, Guid> PilotByFrequency { get; set; }
            public Dictionary<string, StoredChannel> Channels { get; set; }
        }

        private class StoredChannel
        {
            public int[] TimesMs { get; set; }
            public int[] Counts { get; set; }
        }

        private string PathFor(Guid raceId)
        {
            string directory = Directory;
            return directory == null ? null : System.IO.Path.Combine(directory, raceId + ".json");
        }

        // Once per race. File IO must never take a race down with it, hence the blanket catch -
        // a missing waveform costs the marshal graph and nothing else.
        private void Save(int markerThreshold)
        {
            StoredWaveform stored;

            lock (captureLock)
            {
                RaceCapture race = current;
                if (race == null || race.RaceId == Guid.Empty || race.ByFrequency.Count == 0)
                    return;

                // An aborted start arms and disarms detection with no frames in between.
                if (!race.ByFrequency.Values.Any(c => c.Times.Count > 0))
                    return;

                stored = new StoredWaveform
                {
                    RaceId = race.RaceId,
                    MarkerThreshold = markerThreshold,
                    PilotByFrequency = race.PilotByFrequency.ToDictionary(
                        kvp => kvp.Key.ToString(), kvp => kvp.Value),
                    Channels = race.ByFrequency.ToDictionary(
                        kvp => kvp.Key.ToString(),
                        kvp => new StoredChannel
                        {
                            TimesMs = kvp.Value.Times.Select(t => (int)t.TotalMilliseconds).ToArray(),
                            Counts = kvp.Value.Counts.ToArray(),
                        }),
                };
            }

            string path = PathFor(stored.RaceId);
            if (path == null)
            {
                Log("not saved, no event directory known");
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(stored));

                Log("saved: " + path + " (" + new FileInfo(path).Length / 1024 + " KB)");
            }
            catch (Exception ex)
            {
                Logger.TimingLog?.LogException(this, ex);
                Log("save failed for " + path);
            }
        }

        /// <summary>Caller holds captureLock.</summary>
        private RaceCapture Load(Guid raceId)
        {
            string path = PathFor(raceId);
            if (path == null || !File.Exists(path))
                return null;

            try
            {
                StoredWaveform stored = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<StoredWaveform>(File.ReadAllText(path));

                if (stored?.Channels == null)
                    return null;

                RaceCapture race = new RaceCapture
                {
                    RaceId = raceId,
                    // Sample times were stored relative to it, so nothing needs the real start.
                    Start = DateTime.MinValue,
                    PilotByFrequency = new Dictionary<int, Guid>(),
                    StoredThreshold = stored.MarkerThreshold,
                };

                if (stored.PilotByFrequency != null)
                {
                    foreach (var kvp in stored.PilotByFrequency)
                    {
                        if (int.TryParse(kvp.Key, out int frequency))
                            race.PilotByFrequency[frequency] = kvp.Value;
                    }
                }

                foreach (var kvp in stored.Channels)
                {
                    if (!int.TryParse(kvp.Key, out int frequency)) continue;
                    if (kvp.Value?.TimesMs == null || kvp.Value.Counts == null) continue;

                    ChannelSamples samples = new ChannelSamples();
                    int count = Math.Min(kvp.Value.TimesMs.Length, kvp.Value.Counts.Length);
                    for (int i = 0; i < count; i++)
                    {
                        samples.Times.Add(TimeSpan.FromMilliseconds(kvp.Value.TimesMs[i]));
                        samples.Counts.Add(kvp.Value.Counts[i]);
                    }

                    race.ByFrequency[frequency] = samples;
                }

                Log("loaded from " + path + " (" + race.ByFrequency.Count + " channel(s))");

                return race;
            }
            catch (Exception ex)
            {
                Logger.TimingLog?.LogException(this, ex);
                Log("load failed for " + path);
                return null;
            }
        }

        private void Log(string message)
        {
            Logger.TimingLog?.Log(this, "[ArUco] waveform: " + message);
        }
    }
}
