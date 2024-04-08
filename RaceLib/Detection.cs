using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Tools;

namespace RaceLib
{
    public class Detection : BaseObject
    {
        public int TimingSystemIndex { get; set; }

        public Channel Channel { get; set; }
        public DateTime Time { get; set; }

        public int Peak { get; set; }

        public TimingSystemType TimingSystemType { get; set; }

        public Pilot Pilot { get; set; }

        public int LapNumber { get; set; }
        public bool Valid { get; set; }
        public enum ValidityTypes
        {
            Auto,
            ManualOverride
        }

        public ValidityTypes ValidityType { get; set; }

        public bool IsLapEnd { get; set; }

        
        public int RaceSector { get { return RaceSectorCalculator(LapNumber, TimingSystemIndex); } }

        public bool IsHoleshot { get { return Valid && IsLapEnd && LapNumber == 0; } }

        public int SectorNumber { get { return TimingSystemIndex; } }

        public Detection()
        {
        }

        public Detection(TimingSystemType timingSystemType, int timingSystem, Pilot pilot, Channel channel, DateTime time, int lapNumber, bool isLapEnd, int peak)
        {
            Valid = true;
            Pilot = pilot;
            TimingSystemType = timingSystemType;
            TimingSystemIndex = timingSystem;
            Channel = channel;
            Time = time;
            LapNumber = lapNumber;
            IsLapEnd = isLapEnd;
            Peak = peak;

            ValidityType = ValidityTypes.Auto;
        }

        public override string ToString()
        {
            return "Detection " + Pilot.Name + " L" + LapNumber + " I" + TimingSystemIndex + " RS" + RaceSector + " T" + Time.ToLogFormat() + " s" + RaceSectorCalculator(LapNumber, TimingSystemIndex);
        }

        public static int RaceSectorCalculator(int lapNumber, int timingSystemIndex)
        {
            return (lapNumber * 100) + timingSystemIndex;
        }
    }
}
