using RaceLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Sound
{
    public enum SoundKey
    {
        StartRaceIn,
        RaceStart,
        RaceOver,

        TimesUp,
        TimeRemaining,
        AfterTimesUp,
        AnnounceRace,
        AnnounceRaceResults,
        UntilRaceStart,

        Sector,

        RaceLap,
        RaceDone,

        TimeTrialLap,
        TimeTrialDone,

        PracticeLap,
        CasualLap,

        StandDownCancelled,
        StandDownTimingSystem,

        TimingSystemDisconnected,
        TimingSystemConnected,
        TimingSystemsConnected,

        Holeshot,

        NewLapRecord,
        NewHoleshotRecord,

        NameTest,
        HurryUp,
        PilotChannel,
        PilotResult,

        StaggeredStart,
        StaggeredPilot,

        Detection,

        NoVideoDelayingRace,
    }


    public class Sound
    {
        [System.ComponentModel.Browsable(false)]
        public SoundKey Key { get; set; }
        public bool Enabled { get; set; }
        public string TextToSpeech { get; set; }
        public string Filename { get; set; }

        [DisplayName("TTS speed (-10 to 10)")]
        public int Rate { get; set; }

        [DisplayName("Volume (0 to 100)")]

        public int Volume { get; set; }

        public enum SoundCategories
        {
            Announcements,
            Race,
            Detection,
            Status,
            Records,
        }

        [System.ComponentModel.Browsable(false)]
        public SoundCategories Category { get; set; }

        public Sound()
        {
            Enabled = true;
            Rate = 0;
            Volume = 100;
        }

        [System.ComponentModel.Browsable(false)]
        public bool HasFile
        {
            get
            {
                if (string.IsNullOrEmpty(Filename))
                    return false;

                return File.Exists(Filename);
            }
        }

        public override string ToString()
        {
            return Key.ToString();
        }
    }


}
