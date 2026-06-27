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

        [Category("Detection")]
        [DisplayName("Ignore Lost-Signal Frames")]
        [Description("During a race, feed a black frame to the detector when a racing pilot's channel has lost signal (RF snow or a no-signal screen). Prevents the huge contour counts that cause CPU spikes and false detections. Only affects detection input, not the displayed video.")]
        public bool IgnoreLostSignal { get; set; }

        [Category("Detection")]
        [DisplayName("Lost Signal Threshold")]
        [Description("Signal ratio below which a frame is treated as lost signal (0.0 - 1.0). RF snow ~0.01-0.07, live video ~0.6-0.85. Lower = less aggressive. Only used when Ignore Lost-Signal Frames is enabled.")]
        public float LostSignalThreshold { get; set; }

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

        [Category("Overlay")]
        [DisplayName("Show Signal Ratio")]
        [Description("Draw each channel's live signal ratio and the threshold (measured/threshold) on its overlay. Red when below threshold (treated as lost signal). Useful for tuning Lost Signal Threshold.")]
        public bool ShowSignalRatio { get; set; }

        [Category("Overlay")]
        [DisplayName("Character Flip Vertical")]
        [Description("Render the overlay text (ID / size % / FPS) upside-down. Useful when the camera or display is mounted vertically inverted. Text position is unchanged; only the glyphs are flipped.")]
        public bool CharacterFlipVertical { get; set; }

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
            IgnoreLostSignal = false;
            LostSignalThreshold = 0.4f;
            ShowMarkerBox = true;
            ShowMarkerId = true;
            ShowMarkerSizePercent = true;
            ShowFps = false;
            ShowSignalRatio = false;
            CharacterFlipVertical = true;
        }

        public override string ToString()
        {
            return "ArUco (Marker " + (string.IsNullOrWhiteSpace(MarkerIds) ? "-" : MarkerIds) + ")";
        }
    }
}
