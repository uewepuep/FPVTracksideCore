using LapRF;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Timing.ImmersionRC
{
    public class LapRFTimingUSB : LapRFTiming
    {
        private SerialPort comPort;

        public LapRFTimingUSB()
        {
            timeoutSeconds = 30;
            comPort = null;
        }

        // Probes serial ports for a LapRF puck by sending a real RTC_TIME request and
        // waiting for a validly-CRC'd response - same request the normal Connect() flow uses.
        public static string DetectPort()
        {
            string[] avoidedPorts = { "COM1", "/dev/ttyAMA0", "/dev/ttyAMA10" };

            string[] portNames;
            try
            {
                portNames = SerialPort.GetPortNames();
            }
            catch
            {
                return null;
            }

            foreach (string portName in portNames)
            {
                if (Array.Exists(avoidedPorts, value => value.Equals(portName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (TryProbe(portName))
                {
                    return portName;
                }
            }

            return null;
        }

        private static bool TryProbe(string portName)
        {
            SerialPort candidate = null;
            try
            {
                candidate = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    RtsEnable = true,
                    DtrEnable = true,
                    ReadTimeout = 500,
                    WriteTimeout = 1000
                };
                candidate.Open();

                LapRFProtocol probe = new LapRFProtocol();
                bool gotResponse = false;
                probe.OnRTC += (rtcTime) => { gotResponse = true; };

                byte[] request = probe.requestRTCTime().ToArray();
                candidate.Write(request, 0, request.Length);

                byte[] buffer = new byte[256];
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(2000);
                while (!gotResponse && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        int read = candidate.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            probe.processBytes(buffer, read);
                        }
                    }
                    catch (TimeoutException)
                    {
                        // No bytes yet - keep polling until the deadline.
                    }
                }

                return gotResponse;
            }
            catch
            {
                // Ports in use by another device, or not a LapRF, are expected during a scan.
                return false;
            }
            finally
            {
                candidate?.Dispose();
            }
        }

        public override bool Connect()
        {
            base.Connect();

            if (comPort != null)
            {
                Disconnect();
            }


            try
            {
                string portName = (settings as LapRFSettingsUSB).ComPort;
                if (SerialPort.GetPortNames().Contains(portName))
                {
                    comPort = new SerialPort();
                    comPort.BaudRate = 115200;
                    comPort.RtsEnable = true;
                    comPort.DtrEnable = true;
                    comPort.ReadTimeout = 6000;
                    comPort.WriteTimeout = 12000;

                    comPort.PortName = portName;
                    comPort.Open();

                    if (comPort.IsOpen)
                    {
                        connectionCount++;
                        Connected = true;
                        lastData = DateTime.Now;

                        StartThreads();

                        return true;
                    }
                }

                throw new Exception("Couldn't find " + portName);
            }
            catch (Exception e)
            {
                Tools.Logger.TimingLog.LogException(this, e);
            }


            return false;
        }

        public override bool Disconnect()
        {
            base.Disconnect();

            try
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
            }
            catch (Exception e)
            {
                Tools.Logger.TimingLog.LogException(this, e);
            }
            
            return false;
        }

        protected override bool Send(byte[] data)
        {
            if (comPort == null)
            {
                return false;
            }

            if (!comPort.IsOpen)
            {
                Connected = false;
                return false;
            }

            try
            {
                comPort.Write(data, 0, data.Length);

                // >:( Give the puck time to work. Stupid immersion.
                System.Threading.Thread.Sleep(400);

                base.Send(data);
                return true;
            }
            catch (Exception e)
            {
                Tools.Logger.TimingLog.LogException(this, e);
                return false;
            }
        }

        protected override int Recv(byte[] rxBuf)
        {
            if (comPort == null)
            {
                return 0;
            }
            if (!comPort.IsOpen)
            {
                Connected = false;
                return 0;
            }

            int byte_received = 0;
            try
            {
                byte_received = comPort.Read(rxBuf, 0, rxBuf.Length);

                base.Recv(rxBuf);

                TimeSpan sinceData = DateTime.Now - lastData;

                lastData = DateTime.Now;

                return byte_received;
            }
            catch (Exception e)
            {
                Tools.Logger.TimingLog.LogException(this, e);
                return 0;
            }
        }

    }
}
