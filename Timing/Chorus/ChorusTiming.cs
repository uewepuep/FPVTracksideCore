using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net.Sockets;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Timing.Chorus
{
    public class ChorusTiming : ITimingSystem
    {
        private SerialPort comPort;
        private TcpClient tcpClient;
        private NetworkStream tcpStream;
        private StreamReader tcpReader;
        private StreamWriter tcpWriter;
        private Thread tcpListenerThread;
        private bool isDisposing = false;


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
            tcpClient = null;
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
                if (Chorus32Settings.UseTCP)
                {
                    return ConnectTCP();
                }
                else
                {
                    return ConnectSerial();
                }
            }
            catch (Exception e)
            {
                Connected = false;
                Logger.TimingLog.LogException(this, e);
                return false;
            }
        }

        private bool ConnectSerial()
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
                NodeCount = 8;
                return true;
            }

            return false;
        }

        private bool ConnectTCP()
        {
            tcpClient = new TcpClient();
            tcpClient.Connect(Chorus32Settings.IPAddress, Chorus32Settings.Port);

            if (tcpClient.Connected)
            {
                tcpStream = tcpClient.GetStream();
                tcpReader = new StreamReader(tcpStream, Encoding.ASCII);
                tcpWriter = new StreamWriter(tcpStream, Encoding.ASCII) { AutoFlush = true };

                Connected = true;

                // Start TCP listener thread  
                tcpListenerThread = new Thread(TcpDataReceived)
                {
                    Name = "Chorus32 TCP Listener",
                    IsBackground = true
                };
                tcpListenerThread.Start();

                Send("N0");
                NodeCount = 8;
                return true;
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

        private void TcpDataReceived()
        {
            try
            {
                while (Connected && !isDisposing && tcpClient.Connected)
                {
                    string dataLine = tcpReader.ReadLine();
                    if (dataLine != null)
                    {
                        Parse(dataLine);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!isDisposing)
                {
                    Logger.TimingLog.LogException(this, ex);
                    Connected = false;
                }
            }
        }

        public bool Disconnect()
        {
            Connected = false;
            isDisposing = true;

            if (Chorus32Settings.UseTCP)
            {
                return DisconnectTCP();
            }
            else
            {
                return DisconnectSerial();
            }
        }

        private bool DisconnectSerial()
        {
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

        private bool DisconnectTCP()
        {
            bool success = true;

            try
            {
                if (tcpListenerThread != null && tcpListenerThread.IsAlive)
                {
                    tcpListenerThread.Join(1000);
                }

                tcpWriter?.Close();
                tcpReader?.Close();
                tcpStream?.Close();
                tcpClient?.Close();
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
                success = false;
            }
            finally
            {
                tcpWriter = null;
                tcpReader = null;
                tcpStream = null;
                tcpClient = null;
                tcpListenerThread = null;
            }

            return success;
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
                
                Send(node + "A1"); // activate the node

                // Set the frequency. by setting the band and channel
                // Convert Band enum to numeric value for Chorus32 protocol  
                int bandValue = GetBandValue(frequencySensitivity.Band);

                // Set the band using B{node}{band} command  
                Send(node + "B" + bandValue.ToString() + "0");

                // Set the channel using C{node}{channel} command    
                Send(node + "C" + (frequencySensitivity.Channel - 1).ToString() + "0");

                // Store frequency for detection event mapping  
                nodeTofrequency.Add(index, frequencySensitivity.Frequency);

                // Set the threshold
                Send(node + "T" + threshold.ToString("X4"));

                index++;
            }
            
            while (index < 8)
            {
                string node = "R" + index;
                Send(node + "A0"); // Deactivate unused nodes
                index++;
            }
            return true;
        }

        public bool StartDetection(ref DateTime time, StartMetaData raceMetaData)
        {
            requestStart = DateTime.Now;
            responseStart = DateTime.Now;
            Send("R*R2");
            return Send("R*R2");
        }

        private int GetBandValue(string band)
        {
            // Map band names to Chorus32 numeric values (0-7) as per protocol spec  
            switch (band?.ToUpper())
            {
                case "RACEBAND": return 0;
                case "FATSHARK": return 4;
                case "A": return 1;
                case "B": return 2;
                case "E": return 3;
                case "F":
                case "AIRWAVE": 
                case "D":
                case "LOWBAND": return 5;  // Band D/5.3 is often called LowBand  
                case "CONNEX": return 6;
                case "EXTENDED CONNEX": return 7;
                default: return 0; // Default to Raceband  
            }
        }

        public bool EndDetection(EndDetectionType type)
        {
            Send("R*R0");
            return Send("R*R0");
           
        }

        private bool Send(string data)
        {
            try
            {
                if (Chorus32Settings.UseTCP)
                {
                    if (tcpWriter != null && tcpClient.Connected)
                    {
                        tcpWriter.WriteLine(data);
                        return true;
                    }
                }
                else
                {
                    if (comPort != null && comPort.IsOpen)
                    {
                        comPort.WriteLine(data);
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.TimingLog.LogException(this, e);   
            }
            return false;
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
                /*    int rawMilliseconds = int.Parse(data.Substring(5), System.Globalization.NumberStyles.HexNumber);
                    int node = int.Parse(data[1] + "");

                    DateTime start = new DateTime((requestStart.Ticks + responseStart.Ticks) / 2);

                    DateTime lap = start.AddMilliseconds(rawMilliseconds);

                    int frequency;
                    if (nodeTofrequency.TryGetValue(node, out frequency))
                    {
                        OnDetectionEvent?.Invoke(this, frequency, lap, 200);
                    }
                */
                    if (data.Length >= 6)
                    {
                        int node = int.Parse(data[1].ToString(), System.Globalization.NumberStyles.HexNumber);
                       

                        if (data.Length >= 12) // Ensure we have enough data for 8-digit hex time  
                        {
                            int rawMilliseconds = int.Parse(data.Substring(5), System.Globalization.NumberStyles.HexNumber);

                            DateTime start = new DateTime((requestStart.Ticks + responseStart.Ticks) / 2);
                            DateTime lap = start.AddMilliseconds(rawMilliseconds);

                            int frequency;
                            if (nodeTofrequency.TryGetValue(node, out frequency))
                            {
                                OnDetectionEvent?.Invoke(this, frequency, lap, 200);
                            }
                        }
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
                // RSSI response  
                case 'r':   
                    // Handle RSSI data if needed  
                    break;

                default:
                    throw new Exception("Unknown message");
            }
        }
    }
}
