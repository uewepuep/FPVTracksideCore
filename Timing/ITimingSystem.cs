using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Timing.Chorus;
using Timing.ELRS;
using Timing.ImmersionRC;
using Timing.RotorHazard;
using Timing.Velocidrone;
using Tools;

namespace Timing
{
    public enum TimingSystemType
    {
        Dummy,
        Test,
        LapRF = 2,
        LapRF8Way = 2,
        Video,
        Delta5,
        RotorHazard,
        Chorus,
        Velocidrone,
        Manual,
        Other,
    }

    public class ListeningFrequency
    {
        public int Frequency { get; set; }
        public float SensitivityFactor { get; set; }

        public string Pilot { get; set; }
        public Guid PilotId { get; set; }

        public int Channel { get; set; }
        public string Band { get; set; }

        public Color Color { get; set; }

        /// <summary>Optional simulator pilot identifier (e.g. Velocidrone uid) for pilot mapping.</summary>
        public string SimulatorPilotId { get; set; }

        public ListeningFrequency(string band, int channel, int frequency, float sensitivityFactor, Color color)
            :this("", Guid.Empty, band, channel, frequency, sensitivityFactor, color)
        {
        }

        public ListeningFrequency(string pilot, Guid pilotId, string band, int channel, int freq, float sensitivity, Color color)
        {
            Frequency = freq;
            SensitivityFactor = sensitivity;
            Pilot = pilot;
            PilotId = pilotId;
            Band = band;
            Channel = channel;
            Color = color;
        }

        public override string ToString()
        {
            return Band + " " + Channel + " " + Frequency + "mhz(" + (SensitivityFactor * 100) + "%)";
        }

        public override bool Equals(object obj)
        {
            if (obj as ListeningFrequency == null)
                return false;

            return ((ListeningFrequency)obj).Frequency == Frequency;
        }

        public override int GetHashCode()
        {
            return Frequency;
        }
    }

    public enum TimingSystemRole
    {
        Primary,
        Split
    }

    public enum EndDetectionType
    {
        Normal,
        Abort
    }

    public interface ITimingSystem : IDisposable
    {
        TimingSystemType Type { get; }

        bool Connected { get; }

        /// <summary>Tries to connect to the timing system.</summary>  
        /// <returns> true on success.</returns>
        bool Connect();

        /// <summary> Gracefully disconnects from the timing system.</summary>  
        /// <returns>Return true if it disconnected gracefully.</returns>
        bool Disconnect();

        /// <summary>  
        /// Sets the listening frequencies on the timing system. Frequencies will be given in mhz. Eg 5880 for Raceband 7. 
        /// This will be called at prior to the start of every race.
        ///</summary>  
        /// <returns> Returning true if it set ok. 
        /// Returning false will cancel race start and system will attempt to Connect();
        /// </returns> 
        bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies);

        /// <summary>  
        /// Start the system listening for detection events. </summary>  
        /// <returns> 
        /// Return true if it started ok. 
        /// Returning false will cancel race start and system will attempt to Connect();
        /// </returns>  
        bool StartDetection(ref DateTime time, StartMetaData raceMetaData);

        /// <summary>  
        /// Stops the system listening for detection events.  </summary>  
        /// <returns> 
        /// Return true if it stopped ok.
        /// </returns>  
        bool EndDetection(EndDetectionType type);

        int MaxPilots { get; }

        /// <summary>  
        /// Call this event when a lap has been detected. 
        /// void DetectionEventDelegate(int frequency, DateTime time)
        /// First parameter is frequency in mhz (ie 5880) and the second parameter is the absolute time of the event. 
        /// </summary>  
        event DetectionEventDelegate OnDetectionEvent;

        event MarshallEventDelegate OnMarshallEvent;

        TimingSystemSettings Settings { get; set; }

        IEnumerable<StatusItem> Status { get; }

        string Name { get; }
    }

    /// <summary>
    /// Optional capability for devices that control race state but do not record laps.
    /// Race-control systems are connected and displayed like timing systems, but are
    /// excluded from the primary/split detection pipeline.
    /// </summary>
    public interface IRaceControlTimingSystem : ITimingSystem
    {
        event Action OnRaceStartRequest;
        event Action OnRaceStopRequest;
    }

    public struct StatusItem
    {
        public string Value { get; set; }
        public bool StatusOK { get; set; }
    }

    [XmlInclude(typeof(DummySettings))]
    [XmlInclude(typeof(ELRSSettings))]
    [XmlInclude(typeof(LapRFSettings))]
    [XmlInclude(typeof(LapRFSettingsUSB))]
    [XmlInclude(typeof(LapRFSettingsEthernet))]
    [XmlInclude(typeof(RotorHazardSettings))]
    [XmlInclude(typeof(ChorusSettings))]
    [XmlInclude(typeof(Velocidrone.VelocidroneSettings))]
    [XmlInclude(typeof(Timing.Aruco.ArucoTimingSettings))]
    public class TimingSystemSettings
    {
        [Category("System Settings")]
        public TimingSystemRole Role { get; set; }

        private const string timingSystemFilename = "TimingSystemSettings.xml";

        public override string ToString()
        {
            return GetType().Name;
        }

        public static TimingSystemSettings[] Read(Profile profile)
        {
            try
            {
                TimingSystemSettings[] s = Tools.IOTools.Read<TimingSystemSettings>(profile, timingSystemFilename);
                if (s == null || s.Length == 0)
                {
                    s = new TimingSystemSettings[] { };
                }

                Write(profile, s);

                return s;
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(typeof(TimingSystemSettings), ex);
                return new TimingSystemSettings[] { };
            }
        }

        public static void Write(Profile profile, TimingSystemSettings[] settings)
        {
            Tools.IOTools.Write(profile, timingSystemFilename, settings);
        }
    }

    /// <summary> 
    /// The main delegate for an actual detection event. 
    /// Frequency is mhz, ie 5880
    /// Time is the absolute time of the event. 
    /// Peak is the signal peak
    /// Sector is the sector of the track. 
    /// </summary>  
    public delegate void DetectionEventDelegate(ITimingSystem system, int frequency, DateTime time, int peak);

    public delegate void MarshallEventDelegate(ITimingSystem system, MarshalData marshalData);


    public interface ITimingSystemWithRSSI : ITimingSystem
    {
        IEnumerable<RSSI> GetRSSI();
    }

    /// <summary>
    /// Implemented by timing systems that can accept a marshal correction made locally in
    /// FPVTrackside and push it back out, so the remote system stays the source of truth.
    /// The mirror image of MarshallEventDelegate/OnMarshallEvent, which receives corrections.
    /// </summary>
    public interface IRemoteMarshalUpdatable : ITimingSystem
    {
        void PushMarshalUpdate(MarshalData marshalData);

        /// <summary>
        /// Returns the RSSI waveform/calibration for a pilot run, or null if unavailable (e.g.
        /// RotorHazard hasn't sent it - its ts_race_marshal broadcast doesn't include this yet).
        /// </summary>
        RSSIWaveform GetWaveform(Guid raceId, Guid pilotId);

        /// <summary>
        /// True if this specific connected instance is actually known to support marshalling -
        /// distinct from "this class implements the interface", which just says the feature
        /// exists in FPVTrackside's code. For a remote system (RotorHazard) this depends on
        /// which version of its own connector plugin is deployed there, detected at connect
        /// time - an old plugin predating marshalling should mean the UI doesn't offer it,
        /// rather than silently failing/timing out when clicked. Local-only systems (Dummy)
        /// have no such version concept and are always true.
        /// </summary>
        bool MarshalSupported { get; }
    }

    /// <summary>
    /// Implemented by timing systems that want to know event-level metadata (e.g. the event's
    /// display name), separate from the per-race StartMetaData sent on every race start -
    /// set once, whenever the event is loaded or changed.
    /// </summary>
    public interface IEventAware : ITimingSystem
    {
        void SetEventMetaData(EventMetaData eventMetaData);
    }

    public struct RSSI
    {
        public ITimingSystem TimingSystem { get; set; }
        public int Frequency { get; set; }
        
        public float CurrentRSSI { get; set; }
        public float ScaleMin { get; set; }
        public float ScaleMax { get; set; }
        public bool Detected { get; set; }

    }

    public class MarshalData
    {
        public Guid RaceID { get; set; }
        public string PilotName { get; set; }
        public Guid PilotID { get; set; }
        public MarshalLap[] Laps { get; set; }
    }

    public class MarshalLap
    {
        public int LapNumber { get; set; }
        public bool Valid { get; set; }
        public TimeSpan Length { get; set; }
        public TimeSpan RaceTime { get; set; }
    }

    /// <summary>
    /// Raw RSSI-over-time trace plus current enter/exit calibration for one pilot run, as used
    /// by RotorHazard's own Marshal page to recompute lap crossings. Times are relative to race
    /// start. Not yet populated by any ITimingSystem - RotorHazardTimingSystem will need the
    /// ts_race_marshal broadcast extended (RH-side) to include history_values/history_times/
    /// enter_at/exit_at before this can be filled in from a live race.
    /// </summary>
    public class RSSIWaveform
    {
        public TimeSpan[] Times { get; set; }
        public int[] Values { get; set; }
        public int EnterAt { get; set; }
        public int ExitAt { get; set; }
    }
}

