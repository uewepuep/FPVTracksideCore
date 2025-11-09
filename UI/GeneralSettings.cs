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
        
        [Category("General")]
        [NeedsRestart]
        public string Profile { get; set; }

        [Category("Video")]
        [DisplayName("Enable HLS Streaming")]
        [Description("Enable HTTP Live Streaming for web access. Disable for performance.")]
        public bool HlsEnabled { get; set; }

        [Category("Video Playback")]
        [DisplayName("Slow Playback Speed")]
        [Description("Speed factor for slow playback mode (0.05 to 1.0)")]
        public float SlowPlaybackSpeed { get; set; }

        [Category("Video Playback")]
        [DisplayName("Video Sync Delay")]
        [Description("Sync delay in seconds to compensate for camera latency (0.0 to 2.0)")]
        public float VideoSyncDelay { get; set; }

        public GeneralSettings()
        {
            Profile = "Profile 1";
            HlsEnabled = false; // Disable HLS by default for performance
            SlowPlaybackSpeed = 0.10f; // Default slow speed
            VideoSyncDelay = 0.40f; // Default sync delay
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
