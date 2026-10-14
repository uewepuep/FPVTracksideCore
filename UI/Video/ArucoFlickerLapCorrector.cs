using RaceLib;
using System;
using System.Linq;
using Timing;
using Timing.Aruco;
using Tools;

namespace UI.Video
{
    /// <summary>
    /// Live correction of the extra lap a flickering marker produces.
    ///
    /// A gate pass ends when the marker leaves view. If it drops out for a moment on the way
    /// through, ArUco reports a crossing at the drop-out and another at the real exit, and the
    /// second is the true one. Lapalyser sees the short lap between them and disqualifies the
    /// newer detection, keeping the earlier - the wrong way round for a marker count.
    ///
    /// This flips that: the new lap is kept and the previous one disqualified. It is the same
    /// rule the marshal screen's recalculate button applies afterwards, which is why the two
    /// agree instead of the marshal screen appearing to disagree with the timer.
    ///
    /// Only acts while an ArUco system is the primary, and only when the ArUco settings ask for
    /// it. Nothing here is reachable from any other timing system.
    /// </summary>
    internal class ArucoFlickerLapCorrector : IDisposable
    {
        private readonly EventManager eventManager;
        private readonly RaceManager raceManager;
        private readonly TimingSystemManager timingSystemManager;

        // SetLapValidity raises OnLapDisqualified again, for both of the calls below, so without
        // this the first correction would re-enter and a run of short laps could cascade.
        private bool correcting;

        public ArucoFlickerLapCorrector(EventManager eventManager, TimingSystemManager timingSystemManager)
        {
            this.eventManager = eventManager;
            this.timingSystemManager = timingSystemManager;
            raceManager = eventManager?.RaceManager;

            if (raceManager != null)
            {
                raceManager.OnLapDisqualified += OnLapDisqualified;
            }
        }

        public void Dispose()
        {
            if (raceManager != null)
            {
                raceManager.OnLapDisqualified -= OnLapDisqualified;
            }
        }

        private bool Enabled
        {
            get
            {
                // Every detection in the race comes from the primary, so this is also the test
                // for "these laps are ArUco's".
                ArucoTimingSystem primary = timingSystemManager?.PrimeSystems?
                    .OfType<ArucoTimingSystem>()
                    .FirstOrDefault();

                return primary?.ArucoSettings != null && primary.ArucoSettings.CorrectFlickerLaps;
            }
        }

        private void OnLapDisqualified(Lap lap)
        {
            if (correcting)
                return;

            // Raised for a validation as well as a disqualification.
            if (lap?.Detection == null || lap.Detection.Valid)
                return;

            // Only Lapalyser's automatic call is second-guessed here. A marshal's decision, or an
            // operator unticking a lap, is left exactly as made.
            if (lap.Detection.ValidityType != Detection.ValidityTypes.Auto)
                return;

            if (!raceManager.RaceRunning || !Enabled)
                return;

            Race race = lap.Race;
            if (race == null)
                return;

            TimeSpan minLapTime = eventManager.Event.MinLapTime;
            if (minLapTime <= TimeSpan.Zero)
                return;

            Lap previous = race
                .GetLaps(l => l.Pilot == lap.Pilot
                              && l.Detection != null
                              && l.Detection.Valid
                              && l.Detection.Time < lap.Detection.Time)
                .OrderByDescending(l => l.Detection.Time)
                .FirstOrDefault();

            if (previous == null)
                return;

            // The holeshot is corrected like any other crossing. It is a gate pass, not the
            // race start - that stays Race.Start - so if the marker flickered on the way through
            // the start gate, the later crossing is the true one there too. Race.ReCalculateLaps
            // renumbers from the valid detections in time order, so dropping the old holeshot
            // makes the kept crossing lap 0, and the holeshot simply measures a little longer.
            if (lap.Detection.Time - previous.Detection.Time >= minLapTime)
                return;

            correcting = true;
            try
            {
                // ManualOverride, not Auto: SetDetectionValidity refuses to move a detection from
                // ManualOverride with an Auto call, which is what stops Lapalyser.Analyse - which
                // runs immediately after this, and again on every later lap - from undoing it.
                raceManager.SetLapValidity(lap, true, Detection.ValidityTypes.ManualOverride);
                raceManager.DisqualifyLap(previous, Detection.ValidityTypes.ManualOverride);

                Logger.TimingLog?.Log(this, "[ArUco] flicker lap: kept "
                    + lap.Detection.Time.ToString("HH:mm:ss.fff") + ", dropped "
                    + previous.Detection.Time.ToString("HH:mm:ss.fff") + " ("
                    + (lap.Detection.Time - previous.Detection.Time).TotalSeconds.ToString("0.000")
                    + "s apart, under a " + minLapTime.TotalSeconds.ToString("0.#") + "s min lap time)");
            }
            catch (Exception ex)
            {
                Logger.TimingLog?.LogException(this, ex);
            }
            finally
            {
                correcting = false;
            }
        }
    }
}
