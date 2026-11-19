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
    /// Lap rules for ArUco timing, applied before a crossing reaches RaceManager.
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
    /// Only acts while an ArUco system is the Primary, its Validation Time is above 0 and a race
    /// is running. A dropped crossing is still in the marker-count log, so the marshal screen
    /// can bring it back.
    /// </summary>
    public class ArucoLapRules : IDisposable
    {
        private readonly EventManager eventManager;
        private readonly RaceManager raceManager;
        private readonly TimingSystemManager timingSystemManager;

        // Crossings can arrive on several detection threads at once. Each decides from the laps
        // the others may be changing, so all of them run under this lock.
        private readonly object sync = new object();

        public ArucoLapRules(EventManager eventManager)
        {
            this.eventManager = eventManager;
            raceManager = eventManager.RaceManager;
            timingSystemManager = raceManager.TimingSystemManager;

            // The timing systems are rebuilt on every settings change, so hook each new set.
            timingSystemManager.OnInitialise += Attach;
            Attach();
        }

        public void Dispose()
        {
            timingSystemManager.OnInitialise -= Attach;

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

            ArucoLapKind kind = d.TimingSystemType == TimingSystemType.Manual ? ArucoLapKind.Manual : ArucoLapKind.Crossing;

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

                emit();

                if (decision.RecalculateAfter)
                {
                    raceManager.RecalcuateLaps(pilot, race);
                }
            }
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
