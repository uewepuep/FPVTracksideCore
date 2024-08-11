using ExternalData;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace ExternalData
{
    public class RemoteNotifier : IDisposable
    {
        private WorkQueue workQueue;
        private EventManager eventManager;

        public string URL { get; private set; }
        private JSONDataAccessor JSONDataAccessor;

        private SerialPort serialPort;

        private Type lastExceptionType;

        private JsonSerializerSettings serializerSettings;

        public RemoteNotifier(EventManager eventManager, string url, string comportname)
        {
            this.eventManager = eventManager;
            URL = url;
            eventManager.RaceManager.OnSplitDetection += Detection;
            eventManager.RaceManager.OnLapDetected += RaceManager_OnLapDetected;
            eventManager.RaceManager.OnRaceStart += RaceManager_OnRaceStart;
            eventManager.RaceManager.OnRaceEnd += RaceManager_OnRaceEnd;
            eventManager.RaceManager.OnRacePreStart += RaceManager_OnRacePreStart;
            eventManager.RaceManager.OnRaceCancelled += RaceManager_OnRaceCancelled;
            eventManager.RaceManager.OnPilotAdded += OnPilotsChanged;
            eventManager.RaceManager.OnPilotRemoved += OnPilotsChanged;
            eventManager.RaceManager.OnRaceTimesUp += OnRaceTimesUp;
            eventManager.RaceManager.OnChannelCrashedOut += RaceManager_OnChannelCrashedOut;

            JSONDataAccessor = new JSONDataAccessor();
            workQueue = new WorkQueue("RemoteNotifier");


            serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                DateFormatString = "yyy/MM/dd H:mm:ss"
            };

            string[] ports = SerialPort.GetPortNames();
            if (ports.Contains(comportname))
            {
                serialPort = new SerialPort();
                serialPort.BaudRate = 115200;
                serialPort.RtsEnable = true;
                serialPort.DtrEnable = true;
                serialPort.ReadTimeout = 6000;
                serialPort.WriteTimeout = 12000;

                serialPort.PortName = comportname;
                serialPort.Open();
            }
            else
            {
                serialPort = null;
            }
        }

        public void Dispose()
        {
            if (serialPort != null)
            {
                serialPort.Dispose();
                serialPort = null;
            }

            eventManager.RaceManager.OnSplitDetection -= Detection;
            eventManager.RaceManager.OnLapDetected -= RaceManager_OnLapDetected;
            eventManager.RaceManager.OnRaceStart -= RaceManager_OnRaceStart;
            eventManager.RaceManager.OnRaceEnd -= RaceManager_OnRaceEnd;
            eventManager.RaceManager.OnRacePreStart -= RaceManager_OnRacePreStart;
            eventManager.RaceManager.OnRaceCancelled -= RaceManager_OnRaceCancelled;
            eventManager.RaceManager.OnPilotAdded -= OnPilotsChanged;
            eventManager.RaceManager.OnPilotRemoved -= OnPilotsChanged;
            eventManager.RaceManager.OnRaceTimesUp -= OnRaceTimesUp;


            workQueue.Dispose();
            workQueue = null;
        }

        private void RaceManager_OnChannelCrashedOut(Channel channel, Pilot pilot, bool manual)
        {
            Color color = eventManager.GetChannelColor(channel);
            PilotCrashedOut pilotState = new PilotCrashedOut(pilot, channel, color, URL);
            pilotState.ManuallySet = manual;
            PutObject(pilotState);
        }

        private void OnRaceTimesUp(Race race)
        {
            RaceState raceState = new RaceState(race, URL) { State = "Times Up" };
            PutObject(raceState);
        }

        private void OnPilotsChanged(RaceLib.PilotChannel pilot)
        {
            RaceLib.Race race = eventManager.RaceManager.CurrentRace;
            if (race != null)
            {
                PilotState[] pilotRaceStates = GetPilotRaceStates(race).ToArray();
                PutObject(new PilotRaceState(race, pilotRaceStates, URL));
            }
        }

        private IEnumerable<PilotState> GetPilotRaceStates(RaceLib.Race race)
        {
            foreach (RaceLib.PilotChannel pc in race.PilotChannels)
            {
                if (pc.Channel == null || pc.Pilot == null)
                    continue;

                Color color = eventManager.GetChannelColor(pc.Channel);

                yield return new PilotState(pc, color);
            }
        }

        private void RaceManager_OnLapDetected(RaceLib.Lap lap)
        {
            Detection(lap.Detection);
        }

        private void Detection(RaceLib.Detection det)
        {
            Color color = eventManager.GetChannelColor(det.Channel);

            RaceLib.Race current = eventManager.RaceManager.CurrentRace;
            if (current == null)
            {
                return;
            }

            int position = current.GetPosition(det.Pilot);

            TimeSpan time = det.Time - current.Start;
            DetectionDetails dd = new DetectionDetails(det, color, time, det.LapNumber == current.TargetLaps, URL);
            dd.Position = position;

            PutObject(dd);
        }

        private void RaceManager_OnRacePreStart(RaceLib.Race race)
        {
            RaceState raceState = new RaceState(race, URL) { State = "Arm" };
            PutObject(raceState);
        }

        private void RaceManager_OnRaceEnd(RaceLib.Race race)
        {
            RaceState raceState = new RaceState(race, URL) { State = "End" };
            PutObject(raceState);
        }

        private void RaceManager_OnRaceStart(RaceLib.Race race)
        {
            RaceState raceState = new RaceState(race, URL) { State = "Start" };
            PutObject(raceState);
        }

        private void RaceManager_OnRaceCancelled(RaceLib.Race race, bool failure)
        {
            RaceState raceState = new RaceState(race, URL) { State = "Cancel" };
            PutObject(raceState);
        }
        
        private void PutObject(IRequest request)
        {
            if (!string.IsNullOrEmpty(URL))
            {
                workQueue.Enqueue(() =>
                {
                    try
                    {
                        HTTPResponseResult result = JSONDataAccessor.PutObject(request);
                        if (!result.AsyncWaitHandle.WaitOne(10000))
                        {
                            Logger.HTTP.Log(this, "Timeout");
                        }

                        if (result.Error != null)
                        {
                            Logger.HTTP.LogException(this, result.Error);
                        }
                    }
                    catch (Exception e)
                    {
                        bool newExceptionType = lastExceptionType == null || lastExceptionType != e.GetType();
                        if (newExceptionType)
                        {
                            Logger.HTTP.LogException(this, e);
                            lastExceptionType = e.GetType();
                        }
                    }
                });
            }

            if (serialPort != null)
            {
                workQueue.Enqueue(() =>
                {
                    try
                    {
                        string json = JsonConvert.SerializeObject(request, serializerSettings);
                        byte[] bytes = Encoding.UTF8.GetBytes(json);

                        serialPort.Write(bytes, 0, bytes.Length);
                    }
                    catch (Exception e)
                    {
                        bool newExceptionType = lastExceptionType == null || lastExceptionType != e.GetType();
                        if (newExceptionType)
                        {
                            Logger.HTTP.LogException(this, e);
                            lastExceptionType = e.GetType();
                        }
                    }
                });
            }

        }
    } 

    public class Notification : IRequest
    {
        public DateTime DateTime { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public string URL { get; set; }

        public Notification()
        {
            DateTime = DateTime.Now;
        }
    }

    public class RaceState : Notification
    {
        public int Round { get; set; }
        public int Race { get; set; }
        public string Type { get; set; }

        public string State { get; set; }
        public RaceState(RaceLib.Race race, string url)
        {
            URL = url;
            Round = race.RoundNumber;
            Race = race.RaceNumber;
            Type = race.Type.ToString();
        }
    }

    public class PilotRaceState : Notification
    {
        public int Round { get; set; }
        public int Race { get; set; }
        public string Type { get; set; }

        public PilotState[] Pilots { get; set; }
        public PilotRaceState(RaceLib.Race race , PilotState[] pilots, string url)
        {
            URL = url;
            Round = race.RoundNumber;
            Race = race.RaceNumber;
            Type = race.Type.ToString();

            Pilots = pilots;
        }
    }


    public class PilotCrashedOut : Notification
    {
        public string Name { get; set; }
        public string Phonetic { get; set; }
        public byte ChannelColorR { get; set; }
        public byte ChannelColorG { get; set; }
        public byte ChannelColorB { get; set; }

        public string ChannelBand { get; set; }
        public int ChannelNumber { get; set; }
        public int Frequency { get; set; }

        public bool CrashedOut { get; set; }
        public bool ManuallySet { get; set; }

        public PilotCrashedOut(Pilot p, Channel c, Color color, string url)
        {
            URL = url;
            CrashedOut = true;
            Name = p.Name;
            Phonetic = p.Phonetic;

            ChannelColorR = color.R;
            ChannelColorG = color.G;
            ChannelColorB = color.B;

            ChannelBand = c.Band.ToString();
            ChannelNumber = c.Number;
            Frequency = c.Frequency;
        }
    }

    public class PilotState
    {
        public string Name { get; set; }
        public string Phonetic { get; set; }
        public byte ChannelColorR { get; set; }
        public byte ChannelColorG { get; set; }
        public byte ChannelColorB { get; set; }

        public string ChannelBand { get; set; }
        public int ChannelNumber { get; set; }
        public int Frequency { get; set; }

        public PilotState(RaceLib.PilotChannel pc, Color color)
        {
            Name = pc.Pilot.Name;
            Phonetic = pc.Pilot.Phonetic;

            ChannelColorR = color.R;
            ChannelColorG = color.G;
            ChannelColorB = color.B;

            ChannelBand = pc.Channel.Band.ToString();
            ChannelNumber = pc.Channel.Number;
            Frequency = pc.Channel.Frequency;
        }
    }

    public class DetectionDetails : Notification
    {
        public int TimingSystemIndex { get; set; }
        public int LapNumber { get; set; }

        public string ChannelBand { get; set; }
        public int ChannelNumber { get; set; }
        public int Frequency { get; set; }
        public byte ChannelColorR { get; set; }
        public byte ChannelColorG { get; set; }
        public byte ChannelColorB { get; set; }

        public double Time { get; set; }

        public bool IsLapEnd { get; set; }
        public bool IsRaceEnd { get; set; }

        public string PilotName { get; set; }
        public int Position { get; set; }

        public DetectionDetails(RaceLib.Detection detection, Color color, TimeSpan time, bool raceEnd, string url)
        {
            URL = url;
            TimingSystemIndex = detection.TimingSystemIndex;
            ChannelBand = detection.Channel.Band.ToString();
            ChannelNumber = detection.Channel.Number;
            Frequency = detection.Channel.Frequency;
            ChannelColorR = color.R;
            ChannelColorG = color.G;
            ChannelColorB = color.B;
            Time = time.TotalSeconds;
            IsLapEnd = detection.IsLapEnd;
            PilotName = detection.Pilot.Name;
            LapNumber = detection.LapNumber;
            IsRaceEnd = raceEnd;
        }
    }
}
