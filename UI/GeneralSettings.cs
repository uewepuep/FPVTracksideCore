using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI
{
    public class GeneralSettings
    {
        public static GeneralSettings Instance { get; protected set; }

        public enum OrderTypes
        {
            PositionAndPB,
            Channel
        }

        [Category("General")]
        [DisplayName("Show Welcome Screen")]
        public bool ShowWelcomeScreen2 { get; set; }

        [Category("General")]
        public RaceLib.Units Units { get; set; }

        [Category("Performance")]
        [NeedsRestart]
        public int FrameRateLimit { get; set; }

        [Category("Performance")]
        [NeedsRestart]
        [DisplayName("V-Sync")]
        public bool VSync { get; set; }
        
        [Category("Performance")]
        [NeedsRestart]
        [DisplayName("UI / Font Scale (Percent)")]
        public float InverseResolutionScalePercent { get; set; }

        [Category("Video Recording")]
        public int VideosToKeep { get; set; }
        [Category("Video Recording")]
        [NeedsRestart]
        public string VideoStorageLocation { get; set; }

        [Category("Layout")]
        [Browsable(false)]
        public string Theme { get; set; }

        [Category("Layout")]
        [NeedsRestart]
        [DisplayName("Align FPV feeds to the top")]
        public bool AlignChannelsTop { get; set; }

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
        [Category("Layout")]
        [NeedsRestart]
        public int ShownDecimalPlaces { get; set; }

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

        [Category("StaticDetector")]
        [NeedsRestart]
        public bool VideoStaticDetector { get; set; }
        [Category("StaticDetector")]
        public float CrashThreshold { get; set; }
        [Category("StaticDetector")]
        public float ReactivateThreshold { get; set; }
        [Category("StaticDetector")]
        public float StartDelaySeconds { get; set; }

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

        [Category("Sound")]
        [NeedsRestart]
        public bool NextRaceTimer { get; set; }

        [Category("Sound")]
        [NeedsRestart]
        public int[] NextRaceTimesToAnnounce { get; set; }

        [Category("Web")]
        [DisplayName("Auto Sync race results")]
        public bool AutoSync { get; set; }

        [Category("Web")]
        [DisplayName("HTTP Server")]
        [NeedsRestart]
        public bool HTTPServer { get; set; }

        [Category("Gate / LED POST notifications")]
        [NeedsRestart]
        public string NotificationSerialPort { get; set; }

        [Category("Gate / LED POST notifications")]
        [NeedsRestart]
        public string NotificationURL { get; set; }

        [Category("Gate / LED POST notifications")]
        [NeedsRestart]
        public bool NotificationEnabled { get; set; }

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


        [Category("OBS Remote Control")]
        [NeedsRestart]
        [DisplayName("Enabled")]
        public bool OBSRemoteControlEnabled { get; set; }

        [Category("OBS Remote Control")]
        [NeedsRestart]
        [DisplayName("Host")]
        public string OBSRemoteControlHost { get; set; }
        
        [Category("OBS Remote Control")]
        [NeedsRestart]
        [DisplayName("Port")]
        public int OBSRemoteControlPort { get; set; }
       
        [Category("OBS Remote Control")]
        [NeedsRestart]
        [DisplayName("Password")]
        public string OBSRemoteControlPassword { get; set; }

        [Category("OBS Remote Control")]
        [DisplayName("Scene: Live Pre-Race")]
        public string OBSRemoteControlSceneLivePreRace { get; set; }
        
        [Category("OBS Remote Control")]
        [DisplayName("Scene: Live Mid-Race")]
        public string OBSRemoteControlSceneLiveRace { get; set; }
       
        [Category("OBS Remote Control")]
        [DisplayName("Scene: Live Post-Race")]
        public string OBSRemoteControlSceneLivePostRace { get; set; }

        [Category("OBS Remote Control")]
        [DisplayName("Scene: Rounds")]
        public string OBSRemoteControlSceneRounds { get; set; }
       
        [Category("OBS Remote Control")]
        [DisplayName("Scene: Replay")]
        public string OBSRemoteControlSceneReplay { get; set; }
        [Category("OBS Remote Control")]
        [DisplayName("Scene: Stats / Laptimes / Points / etc")]
        public string OBSRemoteControlSceneStatistics { get; set; }

        public GeneralSettings()
        {
            InverseResolutionScalePercent = 100;
            AutoSync = true;

            Theme = "Dark";
            FrameRateLimit = 60;
            VSync = true;
            
            VideosToKeep = 50;

            VideoStaticDetector = true;

            ReOrderDelaySeconds = 3;
            ReOrderAnimationSeconds = 2;
            AutoHideShowPilotList = true;
            PilotOrderPreRace = OrderTypes.PositionAndPB;
            PilotOrderMidRace = OrderTypes.PositionAndPB;
            PilotOrderPostRace = OrderTypes.PositionAndPB;

            HTTPServer = false;
            AlignChannelsTop = false;
            CropContent16by9 = false;
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


            CrashThreshold = 4;
            ReactivateThreshold = 20;
            StartDelaySeconds = 5;

            ShowSplitTimes = true;
            GridShowPBs = true;

            TopEventName = true;
            TopEventType = true;
            TopLapInfo = true;
            TopRaceTime = true;
            TopRemainingTime = true;
            TopClock = true;
            TopBlank = false;

            RemainingSecondsToAnnounce = new int[] { 0, 10, 30, 60 };
            NextRaceTimesToAnnounce = new int[] { 10, 30, 60, 120 };

            PreRaceScene = true;
            PostRaceScene = true;

            NextRaceTimer = false;

            Voice = "Microsoft Zira Desktop";

            ShowWelcomeScreen2 = true;
            AlwaysShowPosition = false;

            Practice = "Practice";
            TimeTrial = "Time Trial";
            Race = "Race";
            Freestyle = "Freestyle";
            Endurance = "Endurance";
            CasualPractice = "Casual Practice";

            TimeTrialStaggeredStart = false;
            StaggeredStartDelaySeconds = 1;
            ReOrderAtHoleshot = true;

            ShownDecimalPlaces = 2;

            NotificationSerialPort = "";
            VideoStorageLocation = @"Video/";

            OBSRemoteControlEnabled = false;
            OBSRemoteControlHost = "localhost";
            OBSRemoteControlPort = 4455;
            OBSRemoteControlPassword = "42ZzDvzK3Cd43HQW";
        }

        protected const string filename = @"data/GeneralSettings.xml";
        public static GeneralSettings Initialise()
        {
            GeneralSettings generalSettings = null;

            bool error = false;
            try
            {
                GeneralSettings[] s = IOTools.Read<GeneralSettings>(filename);
                
                if (s != null && s.Any())
                {
                    generalSettings = s[0];
                    Write(generalSettings);
                }
                else
                {
                    error = true;
                }
            }
            catch
            {
                error = true;
            }

            if (error)
            {
                GeneralSettings s = new GeneralSettings();
                Write(s);
                generalSettings = s;
            }

            Instance = generalSettings;
            return generalSettings;
        }

        public static void Write(GeneralSettings s)
        {
            IOTools.Write(filename, s);
        }

        public static void Write()
        {
            IOTools.Write(filename, new GeneralSettings[] { Instance });
        }

        public override string ToString()
        {
            return "General Settings";
        }
    }
}
