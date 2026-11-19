using System;
using System.Collections.Generic;
using System.Linq;

namespace Timing.Aruco
{
    public enum ArucoLapKind
    {
        /// <summary>A timer detection: an ArUco crossing, or any other non-manual lap.</summary>
        Crossing,

        /// <summary>A manual lap (Add Lap, lap editor, marshal, ...). Always kept.</summary>
        Manual
    }

    /// <summary>
    /// One valid lap of a pilot, as the lap rules see it. The caller maps its own laps to these
    /// and back through <see cref="Tag"/>, which the rules never look at.
    /// </summary>
    public class ArucoLapEntry
    {
        public DateTime Time { get; }
        public ArucoLapKind Kind { get; }

        /// <summary>Validity set by an operator or marshal: never changed from here.</summary>
        public bool SetByHand { get; }

        public object Tag { get; }

        public ArucoLapEntry(DateTime time, ArucoLapKind kind, bool setByHand = false, object tag = null)
        {
            Time = time;
            Kind = kind;
            SetByHand = setByHand;
            Tag = tag;
        }
    }

    public enum ArucoCrossingAction
    {
        /// <summary>The rules do not apply; hand the crossing over unchanged.</summary>
        PassThrough,

        /// <summary>Do not record the crossing.</summary>
        Drop,

        /// <summary>Record the crossing, after disqualifying Replace.</summary>
        Accept
    }

    public class ArucoCrossingDecision
    {
        public ArucoCrossingAction Action { get; set; }

        /// <summary>For Drop: what the crossing was too close to, for the log.</summary>
        public string DropReason { get; set; }

        /// <summary>For Drop: the time the crossing was measured from.</summary>
        public DateTime Reference { get; set; }

        /// <summary>For Accept: the earlier ArUco lap this crossing corrects, or null.</summary>
        public ArucoLapEntry Replace { get; set; }

        /// <summary>
        /// For Accept: a lap stays after the crossing, which leaves it measured and numbered
        /// from the one before, so the pilot's laps need recalculating.
        /// </summary>
        public bool RecalculateAfter { get; set; }
    }

    /// <summary>
    /// The ArUco lap rules, kept free of RaceLib so they can be tested on their own. W is the
    /// Primary's Validation Time (an ArUco setting, not the event's Min Lap Time); "within W"
    /// means a difference less than W.
    ///
    /// A crossing within W of the previous lap:
    ///  - after a standard manual lap, or a lap set by hand: dropped;
    ///  - after an ArUco lap: dropped, or with Correct Flicker Laps on, the previous one is
    ///    disqualified and this one kept.
    /// A first crossing is always kept: after the start there is only the event's ignore window.
    /// </summary>
    public static class ArucoLapDecisions
    {
        /// <param name="time">The crossing.</param>
        /// <param name="validLaps">The pilot's valid laps.</param>
        /// <param name="pilotStart">The pilot's (handicapped) start.</param>
        /// <param name="raceStartIgnore">The event's ignore window after the start.</param>
        /// <param name="validationTime">W.</param>
        /// <param name="correctFlickerLaps">The Primary's Correct Flicker Laps setting.</param>
        public static ArucoCrossingDecision DecideCrossing(DateTime time, IEnumerable<ArucoLapEntry> validLaps,
            DateTime pilotStart, TimeSpan raceStartIgnore,
            TimeSpan validationTime, bool correctFlickerLaps)
        {
            // RaceManager invalidates a crossing before the start or inside the ignore window
            // itself; that stays exactly as it was.
            if (validationTime <= TimeSpan.Zero || time < pilotStart + raceStartIgnore)
                return new ArucoCrossingDecision { Action = ArucoCrossingAction.PassThrough };

            ArucoLapEntry[] laps = validLaps.ToArray();

            ArucoLapEntry previous = laps
                .Where(l => l.Time < time)
                .OrderByDescending(l => l.Time)
                .FirstOrDefault();

            ArucoLapEntry replace = null;
            string drop = null;

            if (previous != null && time - previous.Time < validationTime)
            {
                if (previous.Kind == ArucoLapKind.Manual)
                    drop = "a manual lap";
                else if (previous.SetByHand)
                    drop = "a lap set by hand";
                else if (correctFlickerLaps)
                    replace = previous;
                else
                    drop = "the previous crossing";
            }

            if (drop != null)
            {
                return new ArucoCrossingDecision
                {
                    Action = ArucoCrossingAction.Drop,
                    DropReason = drop,
                    Reference = previous.Time,
                };
            }

            return new ArucoCrossingDecision
            {
                Action = ArucoCrossingAction.Accept,
                Replace = replace,
                RecalculateAfter = laps.Any(l => l.Time > time && l != replace),
            };
        }
    }
}
