using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Lapalyser
    {

        public TimeSpan MinLapTime { get { return EventManager.Event.MinLapTime; } }

        public RaceManager RaceManager { get; private set; }
        public EventManager EventManager { get; private set; }

        private List<Pilot> withSuspicousLap;

        public Lapalyser(RaceManager racemanager)
        {
            EventManager = racemanager.EventManager;
            RaceManager = racemanager;
            RaceManager.OnRaceStart += ClearSusp;
            RaceManager.OnRaceEnd += ClearSusp;
            withSuspicousLap = new List<Pilot>();
        }

        private void ClearSusp(Race race)
        {
            withSuspicousLap.Clear();
        }

        public void OnLap(Lap lap)
        {
            Pilot pilot = lap.Pilot;

            // don't check holeshots
            if (lap.Detection.IsHoleshot)
                return;

            if (lap.Length < MinLapTime && lap.Detection.TimingSystemType != Timing.TimingSystemType.Manual)
            {
                RaceManager.DisqualifyLap(lap, Detection.ValidityTypes.Auto);
                withSuspicousLap.Add(pilot);
            }

            if (withSuspicousLap.Contains(pilot))
            {
                Analyse(lap.Race, pilot);
            }
        }

        public void Analyse(Race race, Pilot pilot)
        {
            TimeSpan minLapTime = MinLapTime;
            Lap[] laps = race.GetLaps(l => l.Pilot == pilot && !l.Detection.IsHoleshot);

            IEnumerable<Lap> suspects = laps.Where(l => l.Length < minLapTime || !l.Detection.Valid).OrderBy(r => r.Detection.Time);
            foreach (Lap current in suspects)
            {
                Lap prev = laps.Where(l => l.Detection.Time < current.Detection.Time && l.Detection.Valid).OrderByDescending(l => l.Detection.Time).FirstOrDefault();
                if (prev == null)
                {
                    if (current.Detection.Valid)
                    {
                        RaceManager.DisqualifyLap(current, Detection.ValidityTypes.Auto);
                    }
                }
                else
                {
                    Lap next = laps.Where(l => l.Detection.Time > current.Detection.Time).OrderBy(l => l.Detection.Time).FirstOrDefault();
                    if (next != null)
                    {
                        TimeSpan start = TimeSpan.Zero;
                        Lap holeshot = laps.FirstOrDefault(l => l.Detection.IsHoleshot);
                        if (holeshot != null)
                        {
                            start = holeshot.EndRaceTime;
                        }

                        // compare min lap times. the one with the smallest lap time will be the one to disqualify.
                        TimeSpan prevInvalidMin = GetMinLapTime(start, current, next);
                        TimeSpan currentInvalidMin = GetMinLapTime(start, prev, next);

                        if (prevInvalidMin == currentInvalidMin)
                            continue;

                        Lap invalid = prevInvalidMin < currentInvalidMin ? current : prev;
                        Lap valid = invalid == current ? prev : current;

                        RaceManager.SetLapValidity(valid, true, Detection.ValidityTypes.Auto);
                        RaceManager.DisqualifyLap(invalid, Detection.ValidityTypes.Auto);
                    }
                }
            }
        }

        private TimeSpan GetMinLapTime(TimeSpan lapStart, params Lap[] laps)
        {
            TimeSpan min = TimeSpan.MaxValue;
            foreach (Lap lap in laps)
            {
                TimeSpan length = lap.EndRaceTime - lapStart;

                if (length < min)
                    min = length;

                lapStart = lap.EndRaceTime;
            }

            return min;
        }
    }
}
