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
