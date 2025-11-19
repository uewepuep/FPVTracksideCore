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

    public class ApplicationProfileSettings
    {
        public static ApplicationProfileSettings Instance { get; protected set; }
        public static Profile ProfileInstance { get; protected set; }

        public enum OrderTypes
        {
            PositionAndPB,
            Channel
        }

        [Category("General")]
        [DisplayName("Show Welcome Screen")]
        public bool ShowWelcomeScreen { get; set; }

        [Category("General")]
        public RaceLib.Units Units { get; set; }

        [Category("General")]
        [DisplayName("'Sponsored By' messages.")]
        public bool SponsoredByMessages { get; set; }

        [Category("General")]
        [NeedsRestart]
        public int ShownDecimalPlaces { get; set; }


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

        [Category("Performance")]
        [DisplayName("Legacy DirectX 9.0 (Reach) mode")]
        [NeedsRestart]
        public bool UseDirectX9 { get; set; }

        [DisplayName("Video recordings to keep")]
        [Category("Video")]
        public int VideosToKeep { get; set; }
        [Category("Data")]
        [NeedsRestart]
        [DisplayName("Data Storage Location")]
        [Description("macOS: Absolute path (e.g. /Volumes/Drive/Data/) moves ALL data to that location. Relative path (e.g. events/) keeps all data in Application Support. Windows: Only affects events folder.")]
        public string EventStorageLocation { get; set; }

        /// <summary>
        /// Gets EventStorageLocation with ~ expanded to full home path on macOS.
        /// Use this instead of EventStorageLocation when building actual paths.
        /// </summary>
        [Browsable(false)]
        public string EventStorageLocationExpanded
        {
            get
            {
                string path = EventStorageLocation;
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    if (!string.IsNullOrEmpty(path) && path.StartsWith("~"))
                    {
                        string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                        path = homeDir + path.Substring(1);
                    }
                }
                return path;
            }
        }

        [Category("Static Detector")]
        [NeedsRestart]
        public bool VideoStaticDetector { get; set; }
        [Category("Static Detector")]
        public float CrashThreshold { get; set; }
        [Category("Static Detector")]
        public float ReactivateThreshold { get; set; }
        [Category("Static Detector")]
        public float StartDelaySeconds { get; set; }

        [Category("Web")]
        [DisplayName("Auto Sync race results")]
        public bool AutoSync { get; set; }

        [Category("Web")]
        [DisplayName("Auto start HTTP Server")]
        [NeedsRestart]
        public bool HTTPServer { get; set; }

        [Category("Web")]
        [NeedsRestart]
        [DisplayName("HTTP Server race controls enabled")]
        public bool HTTPServerRaceControl { get; set; }


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

        [Category("Layout")]
        public bool AlwaysShowPosition { get; set; }

        [Category("Layout")]
        [DisplayName("Position and Delta time show time seconds")]
        [NeedsRestart]
        public float ShowPositionDeltaTime { get; set; }

        [Category("Layout")]
        public bool AlwaysSmallPilotProfile { get; set; }

        [Category("Layout")]
        public bool ShowDownPilotLapTimes { get; set; }

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

        [Category("Start Rules")]
        public bool TimeTrialStaggeredStart { get; set; }

        [Category("Start Rules")]
        public float StaggeredStartDelaySeconds { get; set; }

        [Category("Start Rules")]
        public bool AutoRaceStartVideoCheck { get; set; }
        [Category("Start Rules")]
        public int AutoRaceStartVideoCheckAnnouncementSeconds { get; set; }

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


        [Category("Pilot Profile")]
        [NeedsRestart]
        public bool PilotProfileChromaKey { get; set; }

        [Category("Pilot Profile")]
        [NeedsRestart]
        public ChromaKeyColor PilotProfileChromaKeyColor { get; set; }
        [Category("Pilot Profile")]
        [NeedsRestart]
        public byte PilotProfileChromaKeyLimit { get; set; }

        [Category("Pilot Profile")]
        [NeedsRestart]
        public int PilotProfilePhotoboothVideoLengthSeconds { get; set; }
        [Category("Pilot Profile")]
        [NeedsRestart]
        public bool PilotProfileRepeatVideo { get; set; }

        [Category("Pilot Profile")]
        [NeedsRestart]
        public bool PilotProfileBoomerangRepeat { get; set; }

        [Category("Pilot Profile")]
        [NeedsRestart]
        public int PilotProfileHoldLengthSeconds { get; set; }

        [Browsable(false)]
        public string Language { get; set; }

        public ApplicationProfileSettings()
        {
            Theme = "FPVTrackside";

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

            StaggeredStartDelaySeconds = 1;
            ReOrderAtHoleshot = true;
            NotificationSerialPort = "";

            CropContent16by9 = false;
            SillyNameChance = 0.05f;

            PilotProfileChromaKey = false;
            PilotProfileChromaKeyColor = ChromaKeyColor.Green;
            PilotProfileChromaKeyLimit = 20;

            PilotProfilePhotoboothVideoLengthSeconds = 5;
            PilotProfileRepeatVideo = true;
            PilotProfileHoldLengthSeconds = 0;
            AutoRaceStartVideoCheck = true;
            AutoRaceStartVideoCheckAnnouncementSeconds = 10;

            InverseResolutionScalePercent = 100;
            AutoSync = true;
            SponsoredByMessages = true;

            FrameRateLimit = 60;
            VSync = true;

            VideosToKeep = 50;
            HTTPServer = false;

            // Platform-specific default paths for first install
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                EventStorageLocation = "~/Documents/FPVTrackside";
            }
            else
            {
                EventStorageLocation = @"events/";
            }

            VideoStaticDetector = true;
            CrashThreshold = 4;
            ReactivateThreshold = 20;
            StartDelaySeconds = 5;
            ShowWelcomeScreen = true;

            ShownDecimalPlaces = 2;
            UseDirectX9 = false;
            ShowPositionDeltaTime = 6;
            Language = "English";
            ShowDownPilotLapTimes = true;
        }

        protected const string filename = "ProfileSettings.xml";

        public static void Initialize(Profile profile)
        {
            Instance = Read(profile);
            ProfileInstance = profile;

            // Set IOTools.EventStorageLocation for macOS custom base directory support
            string eventStorageLocation = Instance?.EventStorageLocation;

            // Expand ~ to full home directory path on macOS
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                if (!string.IsNullOrEmpty(eventStorageLocation) && eventStorageLocation.StartsWith("~"))
                {
                    string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                    eventStorageLocation = homeDir + eventStorageLocation.Substring(1);
                }
            }

            Tools.IOTools.EventStorageLocation = eventStorageLocation;
        }

        public static ApplicationProfileSettings Read(Profile profile)
        {
            string xmlPath = System.IO.Path.Combine(profile.GetPath(), filename);
            string absolutePath = System.IO.Path.GetFullPath(xmlPath);
            Tools.Logger.UI.LogCall(typeof(ApplicationProfileSettings), $"SETTINGS READ - File (relative): {xmlPath}");
            Tools.Logger.UI.LogCall(typeof(ApplicationProfileSettings), $"SETTINGS READ - File (absolute): {absolutePath}");

            ApplicationProfileSettings s = null;
            try
            {
                s = Tools.IOTools.Read<ApplicationProfileSettings>(profile, filename).FirstOrDefault();
                Tools.Logger.UI.LogCall(typeof(ApplicationProfileSettings), $"SETTINGS READ - EventStorageLocation from XML: {s?.EventStorageLocation ?? "null"}");
                if (s == null)
                {
                    s = new ApplicationProfileSettings();
                    Tools.Logger.UI.LogCall(typeof(ApplicationProfileSettings), $"SETTINGS READ - Using defaults: {s.EventStorageLocation}");
                }
            }
            catch (Exception ex)
            {
                s = new ApplicationProfileSettings();
                Tools.Logger.UI.LogCall(typeof(ApplicationProfileSettings), $"SETTINGS READ - Exception ({ex.Message}), using defaults: {s.EventStorageLocation}");
            }

            // Don't modify the loaded value - use it as-is from the XML file
            // The UI should display exactly what's saved, not transform it

            return s;
        }

        public static void Write()
        {
            Write(ProfileInstance, Instance);
        }

        public static void Write(Profile profile, ApplicationProfileSettings profileSettings)
        {
            var stackTrace = new System.Diagnostics.StackTrace(1, true);
            var callerFrame = stackTrace.GetFrame(0);
            string caller = callerFrame?.GetMethod()?.Name ?? "Unknown";

            Tools.Logger.UI.LogCall(typeof(ApplicationProfileSettings), $"SETTINGS WRITE called from {caller} - EventStorageLocation value: {profileSettings?.EventStorageLocation}");

            Tools.IOTools.Write(profile, filename, profileSettings);

            Tools.Logger.UI.LogCall(typeof(ApplicationProfileSettings), $"SETTINGS WRITE completed - Value saved to XML: {profileSettings?.EventStorageLocation}");

            // Update IOTools.EventStorageLocation when settings are written
            if (profileSettings != null)
            {
                string eventStorageLocation = profileSettings.EventStorageLocation;

                // Expand ~ to full home directory path on macOS
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    if (!string.IsNullOrEmpty(eventStorageLocation) && eventStorageLocation.StartsWith("~"))
                    {
                        string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                        eventStorageLocation = homeDir + eventStorageLocation.Substring(1);
                    }
                }

                Tools.IOTools.EventStorageLocation = eventStorageLocation;
            }
        }
    }
}
