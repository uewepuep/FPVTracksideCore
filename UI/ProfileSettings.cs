using Composition.Input;
using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using static UI.GeneralSettings;

namespace UI
{


    public class ProfileSettings
    {
        public static ProfileSettings Instance { get; protected set; }

        [Category("Layout")]
        [Browsable(false)]
        public string Theme { get; set; }

        [Category("Layout")]
        [NeedsRestart]
        [DisplayName("FPV Feeds Alignment")]
        public RectangleAlignment AlignChannels { get; set; }

        [Category("Layout")]
        [DisplayName("Make content 16 by 9 and crop out right side buttons")]
        [NeedsRestart]
        public bool CropContent16by9 { get; set; }

        [Category("Layout")]
        public bool AutoHideShowPilotList { get; set; }

        [Category("Layout")]
        public bool ShowSplitTimes { get; set; }

        [Category("Layout")]
        public bool GridShowPBs { get; set; }

        [Category("Layout")]
        public bool PreRaceScene { get; set; }
        [Category("Layout")]
        public bool PostRaceScene { get; set; }

        [Category("Dynamic pilot ordering")]
        public OrderTypes PilotOrderPreRace { get; set; }
        [Category("Dynamic pilot ordering")]
        public OrderTypes PilotOrderMidRace { get; set; }
        [Category("Dynamic pilot ordering")]
        public OrderTypes PilotOrderPostRace { get; set; }
        [Category("Dynamic pilot ordering")]
        public float ReOrderDelaySeconds { get; set; }
        [Category("Dynamic pilot ordering")]
        public float ReOrderAnimationSeconds { get; set; }

        [Category("Dynamic pilot ordering")]
        public bool AlwaysShowPosition { get; set; }

        [Category("Dynamic pilot ordering")]
        public bool ReOrderAtHoleshot { get; set; }

        [Category("Sound")]
        [Browsable(false)]
        public bool TextToSpeech { get { return TextToSpeechVolume > 0; } }

        [Category("Sound")]
        [DisplayName("Text to speech Volume (0 - 100)")]
        [NeedsRestart]
        public int TextToSpeechVolume { get; set; }

        [Category("Sound")]
        [NeedsRestart]
        public string Voice { get; set; }

        [Category("Sound")]
        [NeedsRestart]
        public int[] RemainingSecondsToAnnounce { get; set; }

        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid1 { get; set; }
        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid2 { get; set; }
        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid3 { get; set; }
        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid4 { get; set; }
        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid6 { get; set; }
        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid8 { get; set; }
        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid10 { get; set; }
        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid12 { get; set; }

        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid15 { get; set; }

        [Category("Allowed Channel Grid Layouts")]
        public bool ChannelGrid16 { get; set; }

        [Category("Top Bar Items")]
        [NeedsRestart]
        public bool TopEventName { get; set; }
        [Category("Top Bar Items")]
        [NeedsRestart]
        public bool TopEventType { get; set; }
        [NeedsRestart]
        [Category("Top Bar Items")]
        public bool TopLapInfo { get; set; }
        [NeedsRestart]
        [Category("Top Bar Items")]
        public bool TopRaceTime { get; set; }
        [NeedsRestart]
        [Category("Top Bar Items")]
        public bool TopRemainingTime { get; set; }
        [NeedsRestart]
        [Category("Top Bar Items")]
        [DisplayName("Top Clock (hh:mm)")]
        public bool TopClock { get; set; }
        [NeedsRestart]
        [Category("Top Bar Items")]
        [DisplayName("Blank area for overlays")]
        public bool TopBlank { get; set; }

        [NeedsRestart]
        [Category("Event Type Names")]
        public string Practice { get; set; }
        [NeedsRestart]
        [Category("Event Type Names")]
        public string TimeTrial { get; set; }
        [NeedsRestart]
        [Category("Event Type Names")]
        public string Race { get; set; }
        [NeedsRestart]
        [Category("Event Type Names")]
        public string Freestyle { get; set; }
        [NeedsRestart]
        [Category("Event Type Names")]
        public string Endurance { get; set; }
        [NeedsRestart]
        [Category("Event Type Names")]
        public string CasualPractice { get; set; }

        [Category("Start Rules")]
        public bool TimeTrialStaggeredStart { get; set; }

        [Category("Start Rules")]
        public float StaggeredStartDelaySeconds { get; set; }

        [Category("Gate / LED POST notifications")]
        [NeedsRestart]
        public string NotificationSerialPort { get; set; }

        [Category("Gate / LED POST notifications")]
        [NeedsRestart]
        public string NotificationURL { get; set; }

        [Category("Gate / LED POST notifications")]
        [NeedsRestart]
        public bool NotificationEnabled { get; set; }

        [Category("Fun Stuff")]
        [NeedsRestart]
        public float SillyNameChance { get; set; }

        public ProfileSettings()
        {
            Theme = "Dark";

            AlignChannels = RectangleAlignment.Center;

            ReOrderDelaySeconds = 3;
            ReOrderAnimationSeconds = 2;
            AutoHideShowPilotList = true;
            PilotOrderPreRace = OrderTypes.PositionAndPB;
            PilotOrderMidRace = OrderTypes.PositionAndPB;
            PilotOrderPostRace = OrderTypes.PositionAndPB;

            TextToSpeechVolume = 100;

            ChannelGrid1 = true;
            ChannelGrid2 = true;
            ChannelGrid3 = true;
            ChannelGrid4 = true;
            ChannelGrid6 = true;
            ChannelGrid8 = true;
            ChannelGrid10 = true;
            ChannelGrid12 = true;
            ChannelGrid16 = true;
                        
            ShowSplitTimes = true;
            GridShowPBs = true;

            TopEventName = true;
            TopEventType = true;
            TopLapInfo = true;
            TopRaceTime = true;
            TopRemainingTime = true;
            TopClock = true;

            RemainingSecondsToAnnounce = new int[] { 0, 10, 30, 60 };

            PreRaceScene = true;
            PostRaceScene = true;

            Voice = "Microsoft Zira Desktop";

            Practice = "Practice";
            TimeTrial = "Time Trial";
            Race = "Race";
            Freestyle = "Freestyle";
            Endurance = "Endurance";
            CasualPractice = "Casual Practice";

            StaggeredStartDelaySeconds = 1;
            ReOrderAtHoleshot = true;
            NotificationSerialPort = "";

            CropContent16by9 = true;
            SillyNameChance = 0.05f;
        }

        protected const string filename = "ProfileSettings.xml";

        public static void Initialize(Profile profile)
        {
            Instance = Read(profile);
        }

        public static ProfileSettings Read(Profile profile)
        {
            ProfileSettings s = null;
            try
            {
                s = Tools.IOTools.Read<ProfileSettings>(profile, filename).FirstOrDefault();
                if (s == null)
                {
                    s = new ProfileSettings();
                }
            }
            catch
            {
                s = new ProfileSettings();
            }

            Write(profile, s);

            return s;
        }

        public static void Write(Profile profile, ProfileSettings profileSettings)
        {
            Tools.IOTools.Write(profile, filename, profileSettings);
        }
    }
}
