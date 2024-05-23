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

        [Category("General")]
        [DisplayName("'Sponsored By' messages. Please don't disable. This is how we fund the project.")]
        public bool SponsoredByMessages { get; set; }

        [Category("General")]
        [NeedsRestart]
        public int ShownDecimalPlaces { get; set; }

        [Category("General")]
        [NeedsRestart]
        public string Profile { get; set; }

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
        public string EventStorageLocation { get; set; }

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

        public GeneralSettings()
        {
            InverseResolutionScalePercent = 100;
            AutoSync = true;
            SponsoredByMessages = true;

            FrameRateLimit = 60;
            VSync = true;
            
            VideosToKeep = 50;
            HTTPServer = false;

            EventStorageLocation = @"events/";

            VideoStaticDetector = true;
            CrashThreshold = 4;
            ReactivateThreshold = 20;
            StartDelaySeconds = 5;
            ShowWelcomeScreen2 = true;

            ShownDecimalPlaces = 2;
            Profile = "Profile 1";
            UseDirectX9 = false;
        }

        protected const string filename = "GeneralSettings.xml";
        protected const string directory = "data";
        public static GeneralSettings Initialise()
        {
            GeneralSettings generalSettings = null;

            bool error = false;
            try
            {
                GeneralSettings[] s = IOTools.Read<GeneralSettings>(directory, filename);
                
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
            IOTools.Write(directory, filename, s);
        }

        public static void Write()
        {
            IOTools.Write(directory, filename, new GeneralSettings[] { Instance });
        }

        public override string ToString()
        {
            return "General Settings";
        }
    }
}
