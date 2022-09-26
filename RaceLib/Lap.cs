using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Lap : BaseDBObject
    {
        public delegate void LapDelegate(Lap lap);

        public Detection Detection { get; set; }

        [LiteDB.BsonIgnore]
        public DateTime Start 
        { 
            get 
            { 
                if (Length.TotalDays > 100)
                {
                    return DateTime.MinValue;
                }
                return End - Length;
            }
            set 
            { 
                Length = End - value; 
            } 
        }

        [LiteDB.BsonIgnore()]
        public DateTime End { get { return Detection.Time; } set { Detection.Time = value; } }

        public TimeSpan Length { get; set; }

        [LiteDB.BsonIgnore]
        public TimeSpan EndRaceTime
        {
            get
            {
                return End - Race.Start;
            }
            set
            {
                End = Race.Start + value;

                UpdateLength();

                Lap nextLap = NextLap();
                if (nextLap != null)
                {
                    nextLap.UpdateLength();
                }
            }
        }

        public int Number { get { return Detection.LapNumber; } }

        [LiteDB.BsonIgnore()]
        public Pilot Pilot { get { return Detection.Pilot; } }

        [LiteDB.BsonIgnore()]
        public Race Race { get; set; }

        public Lap()
        {
        }

        public Lap(Race race, DateTime startTime, Detection detection)
            :this()
        {
            Race = race;
            Detection = detection;
            Start = startTime;
        }

        public void UpdateLength()
        {
            DateTime lapStart = Race.GetRaceStartTime(Pilot);
            Lap prevLap = PreviousLap();
            if (prevLap != null)
            {
                lapStart = prevLap.End;
            }

            Length = End - lapStart;
        }

        public Lap PreviousLap()
        {
            return Race.GetValidLaps(Pilot, true).Where(l => l.End < End).OrderByDescending(l => l.End).FirstOrDefault();
        }

        public Lap NextLap()
        {
            return Race.GetValidLaps(Pilot, true).Where(l => l.End > End).OrderBy(l => l.End).FirstOrDefault();
        }

        public override string ToString()
        {
            return Pilot.Name + " Race " + Race.RaceNumber + " Lap " + Number + (!Detection.Valid ? " (Invalid)": "");
        }

        public static string LapNumberToString(int number)
        {
            if (number == 0)
            {
                return "HS";
            }
            else
            {
                return "L" + number;
            }
        }
    }
}
