using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using static RaceLib.GameType;

namespace RaceLib
{
    public enum TimingSystemPointMode
    {
        None = 0,
        PointForDetection
    }

    public class GameType
    {
        public string Name { get; set; }
        public string Description { get; set; }

        public TimingSystemPointMode TimingSystemPointMode { get; set; }

        public int PilotsPerTeam { get; set; }
        public int TargetPoints { get; set; }

        public GameType() 
        {
            Name = "New Game Type";
            Description = "";

            TimingSystemPointMode = TimingSystemPointMode.None;
            PilotsPerTeam = 1;
            TargetPoints = 5;
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
                    s = Tools.IOTools.Read<GameType>(profile, filename).Where(c => c != null).ToArray();
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
            Tools.IOTools.Write(profile, filename, s.ToArray());
        }
    }
}
