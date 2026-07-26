using System;
using System.Collections.Generic;
using Tools;

namespace Timing.ELRS
{
    /// <summary>
    /// Receives race start/stop commands from an ExpressLRS timer backpack.
    /// The device controls race state only; lap detections still come from a
    /// separately configured primary timing system.
    /// </summary>
    public class ELRSTimingSystem : IRaceControlTimingSystem
    {
        private ELRSSettings settings;
        private readonly VRXCProtocol protocol;
        private DateTime lastStartTriggerTime;
        private DateTime lastStopTriggerTime;
        private string backpackVersion;

        public TimingSystemType Type => TimingSystemType.Other;
        public string Name => "ELRS";
        public bool Connected => protocol.IsConnected;
        public int MaxPilots => 0;

        public TimingSystemSettings Settings
        {
            get => settings;
            set => settings = value as ELRSSettings ?? new ELRSSettings();
        }

        // Required by ITimingSystem. Race-control systems are excluded from the
        // detection pipeline by TimingSystemManager, so these events are unused.
        public event DetectionEventDelegate OnDetectionEvent;
        public event MarshallEventDelegate OnMarshallEvent;

        public event Action OnRaceStartRequest;
        public event Action OnRaceStopRequest;

        public IEnumerable<StatusItem> Status
        {
            get
            {
                yield return new StatusItem
                {
                    StatusOK = Connected,
                    Value = Connected ? $"Connected ({settings.ComPort})" : "Disconnected"
                };

                if (Connected && !string.IsNullOrWhiteSpace(backpackVersion))
                {
                    yield return new StatusItem
                    {
                        StatusOK = true,
                        Value = $"Firmware {backpackVersion}"
                    };
                }

                if (Connected)
                {
                    yield return new StatusItem
                    {
                        StatusOK = true,
                        Value = "Race control ready"
                    };
                }
            }
        }

        public ELRSTimingSystem()
        {
            settings = new ELRSSettings();
            protocol = new VRXCProtocol();
            lastStartTriggerTime = DateTime.MinValue;
            lastStopTriggerTime = DateTime.MinValue;

            protocol.OnStartRaceCommand += HandleStartRaceCommand;
            protocol.OnStopRaceCommand += HandleStopRaceCommand;
            protocol.OnBackpackVersion += HandleBackpackVersion;
            protocol.OnError += HandleError;
        }

        public bool Connect()
        {
            if (string.IsNullOrWhiteSpace(settings.ComPort) || settings.ComPort == "None")
            {
                Logger.TimingLog.Log(this, "Connection failed", "No serial port configured", Logger.LogType.Error);
                return false;
            }

            Logger.TimingLog.Log(this, "Connecting", settings.ComPort, Logger.LogType.Notice);
            bool connected = protocol.Connect(settings.ComPort, settings.BaudRate);

            if (connected)
            {
                Logger.TimingLog.Log(this, "Connected", $"{settings.ComPort} @ {settings.BaudRate} baud", Logger.LogType.Notice);
            }

            return connected;
        }

        public bool Disconnect()
        {
            protocol.Disconnect();
            backpackVersion = null;
            return true;
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            return true;
        }

        public bool StartDetection(ref DateTime time, StartMetaData raceMetaData)
        {
            return Connected;
        }

        public bool EndDetection(EndDetectionType type)
        {
            return true;
        }

        private bool IsDebounced(ref DateTime lastTriggerTime)
        {
            DateTime now = DateTime.UtcNow;
            int debounceMs = Math.Max(0, settings.DebounceMs);

            if ((now - lastTriggerTime).TotalMilliseconds < debounceMs)
            {
                return true;
            }

            lastTriggerTime = now;
            return false;
        }

        private void HandleStartRaceCommand()
        {
            if (IsDebounced(ref lastStartTriggerTime))
            {
                Logger.TimingLog.Log(this, "Debounce", "Start command ignored", Logger.LogType.Notice);
                return;
            }

            Logger.TimingLog.Log(this, "ELRS command", "Start race", Logger.LogType.Notice);
            OnRaceStartRequest?.Invoke();
        }

        private void HandleStopRaceCommand()
        {
            if (IsDebounced(ref lastStopTriggerTime))
            {
                Logger.TimingLog.Log(this, "Debounce", "Stop command ignored", Logger.LogType.Notice);
                return;
            }

            Logger.TimingLog.Log(this, "ELRS command", "Stop race", Logger.LogType.Notice);
            OnRaceStopRequest?.Invoke();
        }

        private void HandleBackpackVersion(string version)
        {
            backpackVersion = version;
            Logger.TimingLog.Log(this, "Backpack version", version, Logger.LogType.Notice);
        }

        private void HandleError(string error)
        {
            Logger.TimingLog.Log(this, "ELRS error", error, Logger.LogType.Error);
        }

        public void Dispose()
        {
            protocol.OnStartRaceCommand -= HandleStartRaceCommand;
            protocol.OnStopRaceCommand -= HandleStopRaceCommand;
            protocol.OnBackpackVersion -= HandleBackpackVersion;
            protocol.OnError -= HandleError;
            protocol.Dispose();
        }
    }
}
