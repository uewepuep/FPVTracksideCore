using Composition.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Timing;
using Timing.Aruco;
using Tools;

namespace UI.Video
{
    /// <summary>
    /// Lap rules for ArUco timing, applied before a crossing reaches RaceManager, plus the
    /// provisional manual lap that gives way to ArUco.
    ///
    /// Lapalyser's automatic check is written for RF timers: of two detections closer than the
    /// Min Lap Time it cannot tell which is false, so it guesses from the neighbouring lap
    /// lengths. ArUco knows: a gate pass ends when the marker leaves view, so a marker that
    /// drops out on the way through reports an early crossing and then the real one. So ArUco
    /// laps skip that check (RaceManager leaves Video laps out) and are decided here instead,
    /// against the Primary's Validation Time setting rather than the event's Min Lap Time.
    ///
    /// The rules themselves are in <see cref="ArucoLapDecisions"/>, which knows nothing of
    /// RaceLib and is tested on its own (Timing.Tests). This class maps the race to it and
    /// applies what it decides.
    ///
    /// While an ArUco race is running, the live manual laps - the Add Lap shortcuts (Alt + 1-8
    /// by default), Alt + click on a channel and Add Lap Now - are taken over: the lap they
    /// record is provisional and gives way to ArUco instead of always being kept. Outside a
    /// race, on the replay and in the lap editor they stay the standard manual lap.
    ///
    /// Only acts while an ArUco system is the Primary, its Validation Time is above 0 and a race
    /// is running. A dropped crossing is still in the marker-count log, so the marshal screen
    /// can bring it back.
    /// </summary>
    public class ArucoLapRules : IDisposable
    {
        private readonly EventManager eventManager;
        private readonly RaceManager raceManager;
        private readonly TimingSystemManager timingSystemManager;

        // Crossings arrive on the detection threads and manual laps on the UI thread. Each decides
        // from the laps the others may be changing, so all of them run under this lock.
        private readonly object sync = new object();

        // Laps entered with Add Lap during an ArUco race. Both kinds of manual lap are
        // TimingSystemType.Manual, so this is the only way to tell the ones that give way to ArUco
        // from the ones that never do.
        // By reference: BaseObject hashes on ID, which the database only assigns when the lap is
        // recorded, after the detection has gone into the set.
        private readonly HashSet<Detection> provisionalDetections = new HashSet<Detection>(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The open event's instance, for the places outside EventLayer that add a lap by hand
        /// (Alt + click on a channel, Add Lap Now).
        /// </summary>
        public static ArucoLapRules Instance { get; private set; }

        public ArucoLapRules(EventManager eventManager)
        {
            this.eventManager = eventManager;
            raceManager = eventManager.RaceManager;
            timingSystemManager = raceManager.TimingSystemManager;

            // The timing systems are rebuilt on every settings change, so hook each new set.
            timingSystemManager.OnInitialise += Attach;
            raceManager.OnRaceStart += ClearProvisionalLaps;
            raceManager.OnRaceReset += ClearProvisionalLaps;
            Attach();

            Instance = this;
        }

        public void Dispose()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            timingSystemManager.OnInitialise -= Attach;
            raceManager.OnRaceStart -= ClearProvisionalLaps;
            raceManager.OnRaceReset -= ClearProvisionalLaps;

            foreach (ArucoTimingSystem sys in ArucoSystems)
            {
                if (sys.CrossingHandler == OnCrossing)
                {
                    sys.CrossingHandler = null;
                }
            }
        }

        private IEnumerable<ArucoTimingSystem> ArucoSystems
        {
            get
            {
                return timingSystemManager.TimingSystems?.OfType<ArucoTimingSystem>() ?? Enumerable.Empty<ArucoTimingSystem>();
            }
        }

        private ArucoTimingSystem Primary
        {
            get
            {
                return timingSystemManager.PrimeSystems?.OfType<ArucoTimingSystem>().FirstOrDefault();
            }
        }

        private void Attach()
        {
            // Every instance gets it; ArucoTimingSystem only asks while it is the Primary.
            foreach (ArucoTimingSystem sys in ArucoSystems)
            {
                sys.CrossingHandler = OnCrossing;
            }
        }

        private void ClearProvisionalLaps(Race race)
        {
            lock (sync)
            {
                provisionalDetections.Clear();
            }
        }

        private TimeSpan ValidationTime
        {
            get
            {
                float seconds = Primary?.ArucoSettings?.ValidationSeconds ?? 0;
                return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
            }
        }

        /// <summary>The pilot's valid laps, as ArucoLapDecisions sees them. Tag is the Lap.</summary>
        private ArucoLapEntry[] GetLapEntries(Race race, Pilot pilot)
        {
            return race.GetLaps(l => l.Pilot == pilot && l.Detection != null && l.Detection.Valid)
                .Select(ToEntry)
                .ToArray();
        }

        private ArucoLapEntry ToEntry(Lap lap)
        {
            Detection d = lap.Detection;
            bool setByHand = d.ValidityType != Detection.ValidityTypes.Auto;

            ArucoLapKind kind;
            if (d.TimingSystemType != TimingSystemType.Manual)
                kind = ArucoLapKind.Crossing;
            else if (provisionalDetections.Contains(d) && !setByHand)
                kind = ArucoLapKind.Provisional;
            else
                // Includes a provisional lap an operator has since set by hand: it no longer gives way.
                kind = ArucoLapKind.Manual;

            return new ArucoLapEntry(d.Time, kind, setByHand, lap);
        }

        private void OnCrossing(int frequency, DateTime time, Action emit)
        {
            lock (sync)
            {
                Race race = raceManager.CurrentRace;
                Pilot pilot = race?.GetPilotChannel(frequency)?.Pilot;

                if (race == null || !race.Running || !race.Type.HasLaps() || pilot == null)
                {
                    emit();
                    return;
                }

                ArucoCrossingDecision decision = ArucoLapDecisions.DecideCrossing(
                    time,
                    GetLapEntries(race, pilot),
                    race.GetHandicappedStart(pilot),
                    eventManager.Event.RaceStartIgnoreDetections,
                    ValidationTime,
                    Primary?.ArucoSettings?.CorrectFlickerLaps ?? false);

                switch (decision.Action)
                {
                    case ArucoCrossingAction.PassThrough:
                        emit();
                        return;

                    case ArucoCrossingAction.Drop:
                        Log("dropped " + Describe(pilot, time) + ", " + Seconds(time - decision.Reference) + " after " + decision.DropReason);
                        return;
                }

                if (decision.Replace != null)
                {
                    raceManager.DisqualifyLap((Lap)decision.Replace.Tag, Detection.ValidityTypes.Auto);
                    Log("flicker lap: kept " + Describe(pilot, time) + ", dropped " + decision.Replace.Time.ToString("HH:mm:ss.fff")
                        + " (" + Seconds(time - decision.Replace.Time) + " apart)");
                }

                foreach (ArucoLapEntry provisional in decision.DisqualifyProvisional)
                {
                    raceManager.DisqualifyLap((Lap)provisional.Tag, Detection.ValidityTypes.Auto);
                    Log("replaced provisional lap " + provisional.Time.ToString("HH:mm:ss.fff") + " with " + Describe(pilot, time));
                }

                emit();

                if (decision.RecalculateAfter)
                {
                    raceManager.RecalcuateLaps(pilot, race);
                }
            }
        }

        /// <summary>
        /// The Add Lap shortcuts while an ArUco race is running. Returns true when the key was taken
        /// over, whether or not it recorded a lap; false leaves it to the standard Add Lap.
        /// </summary>
        public bool OnKeyboardInput(KeyboardInputEvent inputEvent, KeyboardShortcuts keyMapper, bool onReplay)
        {
            int index = keyMapper.AddLapChannelGroup.ToList().FindIndex(k => k != null && k.Match(inputEvent));
            if (index < 0)
                return false;

            // Replays are corrected with the standard manual lap.
            if (onReplay || !TakesOverManualLaps)
                return false;

            Channel[][] channelGroups = eventManager.Channels.GetChannelGroups().ToArray();
            if (index < channelGroups.Length)
            {
                Channel[] group = channelGroups[index];
                PilotChannel pilotChannel = raceManager.CurrentRace?.PilotChannelsSafe.FirstOrDefault(p => group.Contains(p.Channel));
                if (pilotChannel != null)
                {
                    AddManualLap(pilotChannel.Pilot, inputEvent.Creation);
                }
            }
            return true;
        }

        private bool TakesOverManualLaps
        {
            get
            {
                Race race = raceManager.CurrentRace;
                return ValidationTime > TimeSpan.Zero && race != null && race.Running && race.Type.HasLaps();
            }
        }

        /// <summary>
        /// A live manual lap (Add Lap shortcut, Alt + click, Add Lap Now). While an ArUco race is
        /// running it gives way to ArUco and true is returned, whether or not it was recorded;
        /// false leaves it to RaceManager.AddManualLap.
        /// </summary>
        public bool AddManualLap(Pilot pilot, DateTime time)
        {
            if (!TakesOverManualLaps)
                return false;

            try
            {
                lock (sync)
                {
                    AddProvisionalLap(pilot, time);
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog?.LogException(this, ex);
            }
            return true;
        }

        private void AddProvisionalLap(Pilot pilot, DateTime time)
        {
            // Checked again under the lock: the race may have just ended.
            Race race = raceManager.CurrentRace;
            if (race == null || !race.Running || !race.Type.HasLaps())
                return;

            Channel channel = pilot == null ? null : race.GetChannel(pilot);
            if (channel == null)
                return;

            ArucoLapEntry blocking = ArucoLapDecisions.FindProvisionalBlocker(time, GetLapEntries(race, pilot), ValidationTime);
            if (blocking != null)
            {
                Log("ignored provisional lap " + Describe(pilot, time) + ", " + Seconds(time - blocking.Time)
                    + " after " + (blocking.Kind == ArucoLapKind.Provisional ? "a provisional lap" : "a crossing"));
                return;
            }

            // As RaceManager.AddManualLap, but keeping the detection so it can be recognised later.
            Detection detection = new Detection(TimingSystemType.Manual, 0, pilot, channel, time, raceManager.GetCurrentLapNumber(pilot) + 1, true, 0);
            provisionalDetections.Add(detection);
            raceManager.AddLap(detection);
            raceManager.RecalcuateLaps(pilot, race);

            Log("Provisional lap " + Describe(pilot, time));
        }

        private static string Describe(Pilot pilot, DateTime time)
        {
            return time.ToString("HH:mm:ss.fff") + " (" + pilot.Name + ")";
        }

        private static string Seconds(TimeSpan span)
        {
            return span.TotalSeconds.ToString("0.000") + "s";
        }

        private void Log(string text)
        {
            Logger.TimingLog?.Log(this, "[ArUco] " + text);
        }
    }
}
