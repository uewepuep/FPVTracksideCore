using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Timing.Chorus
{
    public class ChorusTiming : ITimingSystem
    {
        private SerialPort comPort;


        public TimingSystemType Type
        {
            get
            {
                return TimingSystemType.Chorus;
            }
        }

        public bool Connected { get; private set; }

        public TimingSystemSettings Settings { get; set; }

        public event DetectionEventDelegate OnDetectionEvent;
        public event MarshallEventDelegate OnMarshallEvent;

        public ChorusSettings Chorus32Settings { get { return Settings as ChorusSettings; } set { Settings = value; } }


        public int NodeCount { get; private set; }

        private float voltage;

        private Dictionary<int, int> nodeTofrequency;

        public IEnumerable<StatusItem> Status
        {
            get
            {
                if (Connected)
                {
                    if (voltage != 0)
                    {
                        yield return new StatusItem() { StatusOK = voltage > 14, Value = voltage + "v" };
                    }
                }
                else
                {
                    yield return new StatusItem() { StatusOK = false, Value = "Discon" };
                }
            }
        }

        public ChorusTiming()
        {
            comPort = null;
            nodeTofrequency = new Dictionary<int, int>();
        }

        private DateTime requestStart;
        private DateTime responseStart;

        public int MaxPilots { get { return NodeCount; } }

        public string Name
        {
            get
            {
                return "C32";
            }
        }

        public bool Connect()
        {
            try
            {
                comPort = new SerialPort();
                comPort.PortName = Chorus32Settings.ComPort;
                comPort.DataReceived += ComPort_DataReceived;
                comPort.BaudRate = 115200;
                comPort.RtsEnable = true;
                comPort.DtrEnable = true;
                comPort.ReadTimeout = 6000;
                comPort.WriteTimeout = 12000;

                comPort.Open();

                if (comPort.IsOpen)
                {
                    Connected = true;

                    Send("N0");

                    return true;
                }
            }
            catch (Exception e)
            {
                Connected = false;
                Logger.TimingLog.LogException(this, e);
                return false;
            }

            return false;
        }

        private void ComPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string dataLine;
                do
                {
                    dataLine = comPort.ReadLine();

                    Parse(dataLine);
                } 
                while (comPort.BytesToRead != 0);
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        public bool Disconnect()
        {
            Connected = false;

            if (comPort == null)
            {
                return false;
            }

            if (comPort.IsOpen)
            {
                comPort.Close();
                comPort = null;
                return true;
            }
            comPort = null;
            return false;
        }

        public void Dispose()
        {
            Disconnect();
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            nodeTofrequency.Clear();

            // Set the min laptime on all.
            Send("R*M" + Chorus32Settings.MinLapTimeSeconds.ToString("X2"));
            
            int index = 0;
            foreach (ListeningFrequency frequencySensitivity in newFrequencies)
            {
                string node = "R" + index;

                int threshold = Chorus32Settings.Threshold;

                if (frequencySensitivity.SensitivityFactor > 0)
                {
                    threshold = (int)(threshold * 1 / frequencySensitivity.SensitivityFactor);
                }

                // Set the frequency.
                Send(node + "F" + frequencySensitivity.Frequency.ToString("X4"));
                nodeTofrequency.Add(index, frequencySensitivity.Frequency);

                // Set the threshold
                Send(node + "T" + threshold.ToString("X4"));

                index++;
            }

            return false;
        }

        public bool StartDetection(ref DateTime time, StartMetaData raceMetaData)
        {
            requestStart = DateTime.Now;
            responseStart = DateTime.Now;
            return Send("R*R2");
        }


        public bool EndDetection()
        {
            return Send("R*R0");
        }

        private bool Send(string data)
        {
            try
            {
                comPort.WriteLine(data);
                return true;
            }
            catch (Exception e)
            {
                Logger.TimingLog.LogException(this, e);
                return false;
            }
        }

        private void Parse(string data)
        {
            // Min length of a request is 2.
            if (data.Length < 2)
                return;

            char firstChar = data[0];

            switch (firstChar)
            {
                case 'N':
                    int count;
                    if (int.TryParse(data[1] + "", out count))
                    {
                        NodeCount = count;
                    }
                    break;
                case 'S':
                    ParseReponse(data);
                    break;
                case 'R':
                    break;

                default:
                    throw new Exception("Unknown message");
            }
        }

        private void ParseReponse(string data)
        {
            if (data.Length < 3)
                return;

            char responseType = data[2];

            switch (responseType)
            {
                //Start race confirmed
                case 'R':
                    responseStart = DateTime.Now;
                    break;

                // Voltage
                case 'v':
                    int rawVoltage = int.Parse(data.Substring(3), System.Globalization.NumberStyles.HexNumber);
                    voltage = rawVoltage * 55 / 1024.0f;
                    break;
                // Lap record
                case 'L':
                    int rawMilliseconds = int.Parse(data.Substring(3), System.Globalization.NumberStyles.HexNumber);
                    int node = int.Parse(data[1] + "");

                    DateTime start = new DateTime((requestStart.Ticks + responseStart.Ticks) / 2);

                    DateTime lap = start.AddMilliseconds(rawMilliseconds);

                    int frequency;
                    if (nodeTofrequency.TryGetValue(node, out frequency))
                    {
                        OnDetectionEvent?.Invoke(this, frequency, lap, 200);
                    }
                    break;

                // Min lap time response
                case 'M':
                    break;
                // Frequency set response
                case 'F':
                    break;
                // Threshold set response
                case 'T':
                    break;

                default:
                    throw new Exception("Unknown message");
            }
        }
    }
}
