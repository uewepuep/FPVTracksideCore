using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tools;

namespace ImageServer
{

    public enum Splits
    {
        SingleChannel,
        TwoByTwo,
        ThreeByTwo,
        ThreeByThree,
        ThreeByFour,
        FourByTwo,
        FourByThree,
        FourByFour,
        Custom
    }

    public enum OverlayAlignment
    {
        TopLeft, TopRight,
        BottomLeft, BottomRight
    }

    public enum FlipMirroreds
    {
        None,
        Flipped,
        Mirrored,
        FlippedAndMirrored
    }

    public class VideoConfig
    {
        [System.ComponentModel.ReadOnly(true)]
        private string deviceName;
        
        [Category("Device")]
        [System.ComponentModel.Browsable(false)]
        public string DeviceName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(FilePath))
                {
                    return FilePath;
                }

                if (string.IsNullOrWhiteSpace(deviceName))
                {
                    return "Unknown Device";
                }

                return deviceName;
            }
            set
            {
                deviceName = value;
            }
        }

        [Category("Device")]
        [DisplayName("Any USB Port")]
        public bool AnyUSBPort { get; set; }

        [System.ComponentModel.Browsable(false)]
        public string FilePath { get; set; }

        [System.ComponentModel.ReadOnly(true)]
        public string DirectShowPath { get; set; }

        [System.ComponentModel.ReadOnly(true)]
        public string MediaFoundationPath { get; set; }
        
        [System.ComponentModel.ReadOnly(true)]
        public string ffmpegId { get; set; }


        [System.ComponentModel.ReadOnly(true)]
        public string URL { get; set; }

        [Category("Device")]
        public Mode VideoMode { get; set; }
        
        [Category("Device")]
        [DisplayName("Flipped / Mirrored")]
        public FlipMirroreds FlipMirrored { get; set; }

        [Browsable(false)]
        public bool Flipped
        {
            get
            {
                return FlipMirrored == FlipMirroreds.Flipped || FlipMirrored == FlipMirroreds.FlippedAndMirrored;
            }
        }
        
        [Browsable(false)]
        public bool Mirrored
        {
            get
            {
                return FlipMirrored == FlipMirroreds.Mirrored || FlipMirrored == FlipMirroreds.FlippedAndMirrored;
            }
        }

        [Category("Device")]
        [DisplayName("Stop feed when not in use")]
        public bool Pauseable { get; set; }

        [Category("Layout")]
        [DisplayName("Channel Splits")]
        public Splits Splits { get; set; }

        [Category("Layout")]
        [DisplayName("Channel Crop Percent")]

        public float ChannelCoveragePercent { get; set; }

        [DisplayName("Missing GMFBridge")]
        [Category("Video Recording")]
        public bool NeedsGMFBridge
        {
            get
            {
                if (FrameWork == FrameWork.DirectShow)
                {
                    return true;
                }
                return false;
            }
        }

        [Category("Video Recording")]
        public bool RecordVideoForReplays { get; set; }

        [Category("Video Recording")]
        public int RecordResolution { get; set; }

        [Category("Video Recording")]
        public int RecordFrameRate { get; set; }

        [Category("Video Recording")]
        [DisplayName("Device Latency (seconds)")]
        public float DeviceLatency { get; set; }


        [System.ComponentModel.Browsable(false)]
        [JsonIgnore]
        [XmlIgnore]
        public FrameTime[] FrameTimes { get; set; }

        [System.ComponentModel.Browsable(false)]
        public FrameWork FrameWork { get; set; }

        public VideoBounds[] VideoBounds { get; set; }

        [System.ComponentModel.Browsable(false)]
        [JsonIgnore]
        [XmlIgnore]
        public bool HasPhotoBooth 
        { 
            get
            {
                return VideoBounds.Any(vb => vb.SourceType == SourceTypes.PhotoBooth);
            }
        }

        public VideoConfig()
        {
            VideoMode = new Mode();
            AnyUSBPort = false;
            FlipMirrored = FlipMirroreds.None;
            Splits = Splits.SingleChannel;
            FilePath = null;
            ChannelCoveragePercent = 99f;
            VideoBounds = new VideoBounds[] { new VideoBounds() };
            Pauseable = true;

            RecordVideoForReplays = false;
            RecordResolution = 480;
            RecordFrameRate = 30;
            FrameTimes = new FrameTime[0];
            DeviceLatency = 0;
        }

        public override string ToString()
        {
            string name = DeviceName;

            if (VideoBounds != null && VideoMode.Index >= 0)
            {
                IEnumerable<SourceTypes> sourceTypesUsed = VideoBounds.Select(vb => vb.SourceType).Distinct();
                if (sourceTypesUsed.Count() == 1)
                {
                    name = name + " (" + sourceTypesUsed.First().ToString() + ")";
                }
            }

            if (AnyUSBPort || DirectShowPath == null)
            {
                return name;
            }
            else
            {
                string hashCode = DirectShowPath.GetHashCode().ToString("X8");
                return name + " #" + hashCode.Substring(0, 2);
            }
        }

        private const string filename = "VideoSettings.xml";

        public VideoConfig Clone()
        {
            VideoConfig c = new VideoConfig();
            c.Splits = Splits;
            c.DirectShowPath = DirectShowPath;
            c.MediaFoundationPath = MediaFoundationPath;
            c.ChannelCoveragePercent = ChannelCoveragePercent;
            c.DeviceName = DeviceName;
            c.FilePath = FilePath;
            c.VideoMode = VideoMode;
            c.Pauseable = Pauseable;
            c.RecordVideoForReplays = RecordVideoForReplays;
            c.FrameWork = FrameWork;
            return c;
        }

        public static VideoConfig[] Read(Profile profile)
        {


            VideoConfig[] s = null;
            try
            {
                s = Tools.IOTools.Read<VideoConfig>(profile, filename);
                if (s == null)
                {
                    s = new VideoConfig[0];
                }

                return s;
            }
            catch
            {
                return new VideoConfig[0];
            }
        }

        public static void Write(Profile profile, VideoConfig[] sources)
        {
            Tools.IOTools.Write(profile, filename, sources);
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            VideoConfig other = obj as VideoConfig;

            if (!string.IsNullOrEmpty(other.FilePath) && other.FilePath == FilePath)
                return true;

            if (!string.IsNullOrEmpty(other.DirectShowPath) && other.DirectShowPath == DirectShowPath)
                return true;

            if (!string.IsNullOrEmpty(other.MediaFoundationPath) && other.MediaFoundationPath == MediaFoundationPath)
                return true;

            return false;
        }

        public override int GetHashCode()
        {
            int hash = 17;
            if (DirectShowPath != null) hash += DirectShowPath.GetHashCode();
            if (MediaFoundationPath != null) hash += MediaFoundationPath.GetHashCode();

            return hash;
        }

        public bool PathContains(string common)
        {
            if (MediaFoundationPath != null && MediaFoundationPath.Contains(common))
            {
                return true;
            }

            if (DirectShowPath != null && DirectShowPath.Contains(common))
            {
                return true;
            }

            return false;
        }
    }

    public enum SourceTypes
    {
        FPVFeed,
        Commentators,
        Launch = 3,
        FinishLine,
        PhotoBooth
    }

    public class VideoBounds
    {
        [System.ComponentModel.Browsable(false)]
        public string Channel { get; set; }
        
        [System.ComponentModel.Browsable(false)]
        public RectangleF RelativeSourceBounds { get; set; }

        [System.ComponentModel.Browsable(false)]
        public SourceTypes SourceType { get; set; }

        [Category("Overlay")]
        public string OverlayText { get; set; }
        
        [Category("Overlay")]
        public OverlayAlignment OverlayAlignment { get; set; }

        [Category("Other")]
        [DisplayName("Show during Races")]
        public bool ShowInGrid { get; set; }
        
        [Category("Other")]
        public bool Crop { get; set; }

        public VideoBounds()
        {
            Crop = false;
            ShowInGrid = false;
            Channel = "";
            RelativeSourceBounds = new RectangleF(0, 0, 1, 1);
            OverlayText = "";
            OverlayAlignment = OverlayAlignment.TopLeft;
            SourceType = SourceTypes.FPVFeed;
        }

        public VideoBounds Clone()
        {
            VideoBounds clone = new VideoBounds();
            clone.Crop = Crop;
            clone.ShowInGrid = ShowInGrid;
            clone.Channel = Channel;
            clone.RelativeSourceBounds = RelativeSourceBounds;
            clone.OverlayText = OverlayText;
            clone.OverlayAlignment = OverlayAlignment;
            clone.SourceType = SourceType;

            return clone;
        }
    }

    public static class ExtV
    {
        public static void GetSplits(this Splits splits, out int HorizontalSplits, out int VerticalSplits)
        {
            switch (splits)
            {
                default:
                case Splits.Custom:
                case Splits.SingleChannel: HorizontalSplits = 1; VerticalSplits = 1; break;
                case Splits.TwoByTwo: HorizontalSplits = 2; VerticalSplits = 2; break;
                case Splits.ThreeByTwo: HorizontalSplits = 3; VerticalSplits = 2; break;
                case Splits.ThreeByThree: HorizontalSplits = 3; VerticalSplits = 3; break;
                case Splits.ThreeByFour: HorizontalSplits = 3; VerticalSplits = 4; break;
                case Splits.FourByTwo: HorizontalSplits = 4; VerticalSplits = 2; break;
                case Splits.FourByThree: HorizontalSplits = 4; VerticalSplits = 3; break;
                case Splits.FourByFour: HorizontalSplits = 4; VerticalSplits = 4; break;
            }
        }
    }
}
