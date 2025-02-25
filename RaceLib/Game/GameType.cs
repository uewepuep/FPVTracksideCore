using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using static RaceLib.Game.GameType;

namespace RaceLib.Game
{
    public enum TimingSystemPointMode
    {
        None = 0,
        PointForDetection,
        CaptureTheTimer
    }

    public class GameType
    {
        public string Name { get; set; }

        public TimingSystemPointMode TimingSystemPointMode { get; set; }

        public int PilotsPerTeam { get; set; }
        public int TargetPoints { get; set; }

        public int[] PointsRemainingWarning { get; set; }

        public GameType()
        {
            Name = "New Game Type";
            TimingSystemPointMode = TimingSystemPointMode.None;
            PilotsPerTeam = 1;
            TargetPoints = 5;
            PointsRemainingWarning = new int[] { 100, 50, 10, 5, 4, 3, 2, 1 };
        }

        public override string ToString()
        {
            if (Name == null)
            {
                return "New Game Type";
            }

            return Name;
        }

        private const string filename = "GameTypes.xml";
        public static GameType[] Read(Profile profile)
        {
            try
            {
                GameType[] s = null;
                try
                {
                    s = IOTools.Read<GameType>(profile, filename).Where(c => c != null).ToArray();
                }
                catch
                {
                }

                if (s == null)
                {
                    s = new GameType[0];
                }

                Write(profile, s);

                return s;
            }
            catch
            {
                return new GameType[0];
            }
        }

        public static void Write(Profile profile, GameType[] s)
        {
            IOTools.Write(profile, filename, s.ToArray());
        }
    }
}
