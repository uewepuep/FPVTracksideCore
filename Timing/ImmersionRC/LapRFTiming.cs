using LapRF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Tools;

namespace Timing.ImmersionRC
{
    public class LapRFTiming : ITimingSystemWithRSSI
    {
        public TimingSystemType Type { get { return TimingSystemType.LapRF; } }

        private LapRFProtocol laprf;

        private Thread polling;


        private Thread lapDetectionReport;
        private List<Tuple<int, DateTime, ushort>> detections;

        private Dictionary<int, int> idToFreq;

        protected LapRFSettings settings;

        public event System.Action OnDataReceived;
        public event System.Action OnDataSent;

        private bool runPolling;

        private DateTime detectionStart;

        public bool Connected { get; protected set; }

        public event DetectionEventDelegate OnDetectionEvent;

        protected DateTime lastData;

        public TimingSystemSettings Settings { get { return settings; } set { settings = value as LapRFSettings; } }

        protected int timeoutSeconds;

        public int MaxPilots
        {
            get
            {
                int i = 0;
                if (settings.Enable1) i++;
                if (settings.Enable2) i++;
                if (settings.Enable3) i++;
                if (settings.Enable4) i++;
                if (settings.Enable5) i++;
                if (settings.Enable6) i++;
                if (settings.Enable7) i++;
                if (settings.Enable8) i++;
                return i;
            }
        }

        public IEnumerable<StatusItem> Status
        {
            get
            {
                float voltage = laprf.getBatteryVoltage();

                yield return new StatusItem() { StatusOK = voltage > settings.VoltageAlarm, Value = voltage.ToString("0.0") + "v" };

                if (connectionCount > 10)
                    yield return new StatusItem() { StatusOK = true, Value = connectionCount.ToString("0") + " disc" };
            }
        }

        protected int connectionCount;

        public string Name
        {
            get
            {
                return "LRF";
            }
        }

        public LapRFTiming()
        {
            detections = new List<Tuple<int, DateTime, ushort>>();
            laprf = new LapRFProtocol();

            idToFreq = new Dictionary<int, int>();
            Connected = false;
            timeoutSeconds = 5;
            connectionCount = 0;
        }

        public void Dispose()
        {
           Disconnect();
        }

        public virtual bool Connect()
        {
            Disconnect();

            laprf = new LapRFProtocol();
            laprf.EpochOldVersionFix = settings.LegacyFirmwareTimeRangeFix;

            OnDataSent?.Invoke();

            return false;
        }

        public virtual bool Disconnect()
        {
            StopThreads();

            OnDataSent?.Invoke();

            Connected = false;
            return true;
        }

        protected void StartThreads()
        {
            runPolling = true;
            polling = new Thread(Poll);
            polling.Name = "LapRF Polling thread.";
            polling.Start();

            lapDetectionReport = new Thread(ReportLaps);
            lapDetectionReport.Name = "LapRF Reporting thread.";
            lapDetectionReport.Start();
        }

        protected void StopThreads()
        {
            runPolling = false;

            if (polling != null)
            {
                polling.Join();
                polling = null;
            }

            if (lapDetectionReport != null)
            {
                lapDetectionReport.Join();
                lapDetectionReport = null;
            }
        }

        protected virtual int Recv(byte[] rxBuf)
        {
            return 0;
        }

        private void Poll()
        {
            while (runPolling)
            {
                Thread.Sleep(10);

                try
                {
                    byte[] rxBuf = new byte[256];
                    int numBytes = Recv(rxBuf);
                    if (numBytes > 0)
                    {
                        laprf.processBytes(rxBuf, numBytes);
                        OnDataReceived?.Invoke();
                    }

                    while (laprf.getPassingRecordCount() > 0)
                    {
                        PassingRecord passingRecord = laprf.getNextPassingRecord();

                        // ignore all timing events before the race start min
                        if (detectionStart + settings.RaceStartMinTime > DateTime.Now)
                        {
                            continue;
                        }

                        if (passingRecord.bValid)
                        {
                            double ms = passingRecord.rtcTime / 1000.0;
                            DateTime epoch = laprf.RTCEpoch;
                            DateTime detectionTime = epoch.AddMilliseconds(ms);

                            // Occasionally passing record sends weird numbers. Ignore them.
                            if (passingRecord.pilotId < 1 || passingRecord.pilotId > 8)
                            {
                                Logger.TimingLog.Log(this, "Invalid Passing Record Pilot ID", string.Join(", ", passingRecord.pilotId, passingRecord.rtcTime, detectionTime, epoch), Logger.LogType.Error);
                                continue;
                            }
                            // Just do a quick sanity check. Times should be now +- 30 seconds.
                            TimeSpan diff = DateTime.Now - detectionTime;
                            if (Math.Abs(diff.TotalSeconds) > 30.0)
                            {
                                // Better to ignore this crazy time?
                                Logger.TimingLog.Log(this, "Out of Time Range", string.Join(", ", passingRecord.pilotId, passingRecord.rtcTime, detectionTime, epoch), Logger.LogType.Error);
                                continue;
                            }

                            ushort peak = passingRecord.peak;

                            int freq;
                            if (idToFreq.TryGetValue(passingRecord.pilotId, out freq))
                            {
                                if (freq == -1)
                                    continue;

                                lock (detections)
                                {
                                    Logger.TimingLog.Log(this, "Detection", string.Join(", ", passingRecord.pilotId, passingRecord.rtcTime, detectionTime, epoch), Logger.LogType.Notice);
                                    detections.Add(new Tuple<int, DateTime, ushort>(freq, detectionTime, peak));
                                }
                            }
                            else
                            {
                                Logger.TimingLog.Log(this, "Invalid Passing Record Pilot ID", string.Join(", ", passingRecord.pilotId, passingRecord.rtcTime, detectionTime), Logger.LogType.Error);
                            }
                        }
                    }
                    TimeSpan sinceData = DateTime.Now - lastData;
                    if (sinceData.TotalSeconds > timeoutSeconds)
                    {
                        Connected = false;
                    }
                }
                catch (Exception e)
                {
                    // Just a catch all for anything that goes wrong inside laprf
                    Tools.Logger.TimingLog.LogException(this, e);
                }
            }
        }


        private void ReportLaps()
        {
            while (runPolling)
            {
                Thread.Sleep(10);

                try
                {
                    DateTime time = DateTime.Now.AddSeconds(-settings.DetectionReportDelaySeconds);
                    Tuple<int, DateTime, ushort> toReport = null;
                    lock (detections)
                    {
                        toReport = detections.Where(d => d.Item2 < time).OrderBy(d => d.Item2).FirstOrDefault();
                        detections.Remove(toReport);
                    }

                    if (toReport != null)
                    {
                        OnDetectionEvent?.Invoke(this, toReport.Item1, toReport.Item2, toReport.Item3);
                    }
                }
                catch (Exception e)
                {
                    // Just a catch all for anything that goes wrong inside laprf
                    Tools.Logger.TimingLog.LogException(this, e);
                }
            }
        }

        public bool EndDetection()
        {
            laprf.prepare_sendable_packet(LapRF.LapRFProtocol.laprf_type_of_record.LAPRF_TOR_STATE_CONTROL);
            laprf.append_field_of_record_u8(0x20, 0);                // 0 = stop race
            if (!Send(laprf.finalize_sendable_packet()))
            {
                Logger.TimingLog.Log(this, "Failed End Detection");
            }
            return true;
        }

        public bool StartDetection(ref DateTime time, Guid raceId)
        {
            if (!SetMinLapTime(settings.MinimumTriggerTime))
            {
                Logger.TimingLog.Log(this, "Failed Set Min Lap Time");
                return false;
            }

            if (!RequestRTCTime())
            {
                Logger.TimingLog.Log(this, "Failed RTC Time");
                return false;
            }

            int maxAttempts = 10;
            do
            {
                // Give it some time to settle after the previous commands.
                Thread.Sleep(200);

                laprf.prepare_sendable_packet(LapRF.LapRFProtocol.laprf_type_of_record.LAPRF_TOR_STATE_CONTROL);
                laprf.append_field_of_record_u8(0x20, 1);   // 1 = start race in normal mode
                //laprf.append_field_of_record_u8(0x20, 2);   // 1 = start race in crash mode
                if (Send(laprf.finalize_sendable_packet()))
                {
                    break;
                }
                maxAttempts--;
            }
            while (maxAttempts > 0);
            if (maxAttempts == 0)
            {
                Logger.TimingLog.Log(this, "Failed Start Race");
                return false;
            }

            detectionStart = DateTime.Now;

            laprf.clearPassingRecords();

            return true;
        }

        protected bool SetMinLapTime(TimeSpan time)
        {
            uint minLapTime = (uint)Math.Max(100, time.TotalMilliseconds);

            laprf.prepare_sendable_packet(LapRF.LapRFProtocol.laprf_type_of_record.LAPRF_TOR_SETTINGS);
            laprf.append_field_of_record_u32(0x26, minLapTime);
            return Send(laprf.finalize_sendable_packet()); 
        }

        private bool Send(MemoryStream dataStream)
        {
            byte[] pack = dataStream.ToArray();
            return Send(pack);
        }

        protected virtual bool Send(byte[] data)
        {
            OnDataSent?.Invoke();
            return false;
        }

        private bool RequestRTCTime()
        {
            if (Send(laprf.requestRTCTime()))
            {
                int counter = 0;
                while (laprf.RTCEpoch == DateTime.MinValue && counter < 1000)
                {
                    Thread.Sleep(10);
                    counter++;
                }

                if (laprf.RTCEpoch == DateTime.MinValue)
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            if (settings.IgnoreFrequencies)
            {
                return true;
            }

            IEnumerator<ListeningFrequency> enumerator = newFrequencies.GetEnumerator();
            idToFreq.Clear();

            laprf.prepare_sendable_packet(LapRF.LapRFProtocol.laprf_type_of_record.LAPRF_TOR_RFSETUP);
            for (int index = 0; index < 8; index++)
            {
                Byte slotID = (Byte)(index + 1);
                int frequency = 0;
                float inverseSensitivity = 1;

                bool enabled = settings.Enables[index];
                if (enabled)
                {
                    enabled = enumerator.MoveNext();
                    if (enabled)
                    {
                        ListeningFrequency current = enumerator.Current;
                        frequency = current.Frequency;
                        if (current.SensitivityFactor > 0)
                        {
                            inverseSensitivity = 1 / current.SensitivityFactor;
                        }
                        idToFreq[slotID] = frequency;
                    }
                }

                if (settings.AlwaysEnableReceivers)
                {
                    enabled = true;
                }

                Byte slotEnabled = enabled ? (Byte)1 : (Byte)0;
                UInt16 freqValue = (UInt16)frequency;
                float threshValue = settings.Thresholds[index] * inverseSensitivity;
                UInt16 gainValue = (UInt16)settings.Gains[index];

                laprf.append_field_of_record_u8(0x01, slotID);                  // slot ID
                laprf.append_field_of_record_u16(0x20, slotEnabled);            // Enable
                laprf.append_field_of_record_fl32(0x23, threshValue);           // Threshold
                laprf.append_field_of_record_u16(0x24, gainValue);              // Gain
                laprf.append_field_of_record_u16(0x25, freqValue);              // Frequency
            }

            if (!Send(laprf.finalize_sendable_packet()))
            {
                Logger.TimingLog.Log(this, "Failed Set Listening Frequencies");
                return false;
            }
            return true;
        }

        public IEnumerable<RSSI> GetRSSI()
        {
            for (int index = 0; index < 8; index++)
            {
                int pilotID = index + 1;
                LapRF.LapRFProtocol.laprf_rssi lr = laprf.getRssiPerSlot(pilotID);

                int freq;
                if (idToFreq.TryGetValue(pilotID, out freq))
                {
                    RSSI rssi = new RSSI()
                    {
                        TimingSystem = this,
                        Frequency = freq,
                        CurrentRSSI = lr.lastRssi,
                        ScaleMax = 3000,
                        ScaleMin = 500,
                        Detected = false
                    };
                    yield return rssi;
                }
            }
        }
    }
}
