using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;

namespace Timing.Aruco
{
    public enum ArucoDetectMode
    {
        Original,
        Corrected,
        Hybrid
    }

    public class ArucoTimingSettings : TimingSystemSettings
    {
        [Category("Marker")]
        [DisplayName("Marker IDs")]
        [Description("ArUco marker IDs this instance reacts to. Comma-separated for multiple IDs (e.g., \"0\" or \"0,1\"). Any of the listed IDs will trigger this timing system.")]
        public string MarkerIds { get; set; }

        [XmlIgnore]
        [Browsable(false)]
        public IReadOnlyList<int> EffectiveMarkerIds
        {
            get
            {
                var list = new List<int>();
                if (!string.IsNullOrWhiteSpace(MarkerIds))
                {
                    foreach (var part in MarkerIds.Split(','))
                    {
                        if (int.TryParse(part.Trim(), out int id))
                            list.Add(id);
                    }
                }
                return list;
            }
        }

        [Category("Detection")]
        [Description("Original uses raw frame, Corrected uses undistorted (requires calibration), Hybrid runs both and merges.")]
        public ArucoDetectMode DetectMode { get; set; }

        [Category("Detection")]
        [Description("Consecutive detections required before the system considers the marker in-view.")]
        public int MarkerThreshold { get; set; }

        [Category("Detection")]
        [Description("Milliseconds the marker must stay out of view before a lap/sector crossing is emitted.")]
        public int FlickerLengthMs { get; set; }

        [Category("Detection")]
        [Description("Minimum marker area as percent of frame. Filters small/noisy detections.")]
        public float MinMarkerPercent { get; set; }

        [Category("Detection")]
        [Description("ArUco error correction rate (0.0 - 1.0). Higher is more tolerant but can false-positive.")]
        public float ErrorCorrectionRate { get; set; }

        [Category("Detection")]
        [Description("Pixel distance under which duplicate detections (Hybrid mode) are merged.")]
        public int HybridDistanceThreshold { get; set; }

        [Category("Detection")]
        [DisplayName("Multi-thread Detection")]
        [Description("Run ArUco detection for each channel in parallel (recommended for 4+ cores).")]
        public bool UseMultiThreadDetection { get; set; }

        [Category("Overlay")]
        [DisplayName("Show Marker Box")]
        [Description("Draw the detected marker outline on both live video and Replay recording.")]
        public bool ShowMarkerBox { get; set; }

        [Category("Overlay")]
        [DisplayName("Show Marker ID")]
        [Description("Draw the marker ID label on both live video and Replay recording.")]
        public bool ShowMarkerId { get; set; }

        [Category("Overlay")]
        [DisplayName("Show Marker Size (%)")]
        [Description("Draw the marker area as a percentage of the detection frame.")]
        public bool ShowMarkerSizePercent { get; set; }

        [Category("Overlay")]
        [DisplayName("Show Detection FPS")]
        [Description("Draw the ArUco detection thread's iterations-per-second on each channel's overlay.")]
        public bool ShowFps { get; set; }

        public ArucoTimingSettings()
        {
            MarkerIds = "0";
            DetectMode = ArucoDetectMode.Hybrid;
            MarkerThreshold = 2;
            FlickerLengthMs = 150;
            MinMarkerPercent = 0.1f;
            ErrorCorrectionRate = 0.6f;
            HybridDistanceThreshold = 20;
            UseMultiThreadDetection = true;
            ShowMarkerBox = true;
            ShowMarkerId = true;
            ShowMarkerSizePercent = true;
            ShowFps = false;
        }

        public override string ToString()
        {
            return "ArUco (Marker " + (string.IsNullOrWhiteSpace(MarkerIds) ? "-" : MarkerIds) + ")";
        }
    }
}
