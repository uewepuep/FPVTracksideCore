using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace Timing.ELRS
{
    /// <summary>
    /// MSP v2 serial transport used by ExpressLRS timer backpacks.
    /// </summary>
    public sealed class VRXCProtocol : IDisposable
    {
        private const byte HeaderDollar = (byte)'$';
        private const byte HeaderX = (byte)'X';
        private const byte PacketCommand = (byte)'<';
        private const byte PacketResponse = (byte)'>';
        private const int HeaderLength = 8;
        private const int MaxPayloadLength = 1024;

        private const ushort SetRecordingState = 0x0305;
        private const ushort GetBackpackVersion = 0x0010;

        private const byte RecordingStopped = 0x00;
        private const byte RecordingStarted = 0x01;

        private readonly object connectionLock = new object();
        private readonly object receiveLock = new object();
        private readonly List<byte> receiveBuffer = new List<byte>();

        private SerialPort serialPort;
        private Thread readThread;
        private volatile bool running;

        public event Action OnStartRaceCommand;
        public event Action OnStopRaceCommand;
        public event Action<string> OnBackpackVersion;
        public event Action<string> OnError;

        public bool IsConnected
        {
            get
            {
                lock (connectionLock)
                {
                    return serialPort?.IsOpen == true;
                }
            }
        }

        public bool Connect(string portName, int baudRate = 460800)
        {
            Disconnect();

            SerialPort candidate = null;
            try
            {
                candidate = CreateSerialPort(portName, baudRate);
                candidate.Open();

                if (!TryHandshake(candidate, out string version))
                {
                    OnError?.Invoke($"No ELRS Backpack response on {portName}; check the port and firmware");
                    candidate.Dispose();
                    return false;
                }

                lock (receiveLock)
                {
                    receiveBuffer.Clear();
                }

                lock (connectionLock)
                {
                    serialPort = candidate;
                    running = true;
                    readThread = new Thread(() => ReadLoop(candidate))
                    {
                        IsBackground = true,
                        Name = "ELRS Backpack Reader"
                    };
                    readThread.Start();
                }

                OnBackpackVersion?.Invoke(version);
                return true;
            }
            catch (Exception ex)
            {
                running = false;
                try
                {
                    candidate?.Dispose();
                }
                catch
                {
                }

                OnError?.Invoke($"Failed to connect to {portName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Returns the first serial port that answers a validated backpack version request.
        /// </summary>
        public static string DetectPort(int baudRate = 460800)
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

                try
                {
                    using (SerialPort candidate = CreateSerialPort(portName, baudRate))
                    {
                        candidate.Open();
                        if (TryHandshake(candidate, out _))
                        {
                            return portName;
                        }
                    }
                }
                catch
                {
                    // Ports in use by another device are expected during a scan.
                }
            }

            return null;
        }

        public void Disconnect()
        {
            SerialPort portToClose;
            Thread threadToJoin;

            lock (connectionLock)
            {
                running = false;
                portToClose = serialPort;
                threadToJoin = readThread;
                serialPort = null;
                readThread = null;
            }

            try
            {
                portToClose?.Close();
            }
            catch
            {
            }

            if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
            {
                threadToJoin.Join(1000);
            }

            try
            {
                portToClose?.Dispose();
            }
            catch
            {
            }

            lock (receiveLock)
            {
                receiveBuffer.Clear();
            }
        }

        private static SerialPort CreateSerialPort(string portName, int baudRate)
        {
            return new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 250,
                WriteTimeout = 1000
            };
        }

        private static bool TryHandshake(SerialPort port, out string version)
        {
            version = null;

            Thread.Sleep(200);
            port.DiscardInBuffer();

            byte[] request = BuildPacket(PacketCommand, GetBackpackVersion, Array.Empty<byte>());
            port.Write(request, 0, request.Length);

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(1500);
            List<byte> buffer = new List<byte>();

            while (DateTime.UtcNow < deadline)
            {
                int available = port.BytesToRead;
                if (available == 0)
                {
                    Thread.Sleep(20);
                    continue;
                }

                byte[] incoming = new byte[available];
                int read = port.Read(incoming, 0, incoming.Length);
                for (int i = 0; i < read; i++)
                {
                    buffer.Add(incoming[i]);
                }

                List<MSPPacket> packets = ExtractPackets(buffer, null);
                foreach (MSPPacket packet in packets)
                {
                    if (packet.Type == PacketResponse && packet.Function == GetBackpackVersion)
                    {
                        version = DecodeVersion(packet.Payload);
                        return true;
                    }
                }
            }

            return false;
        }

        private void ReadLoop(SerialPort port)
        {
            byte[] incoming = new byte[256];

            try
            {
                while (running)
                {
                    try
                    {
                        int read = port.Read(incoming, 0, incoming.Length);
                        if (read > 0)
                        {
                            ProcessIncoming(incoming, read);
                        }
                    }
                    catch (TimeoutException)
                    {
                        // A short timeout lets Disconnect stop this thread promptly.
                    }
                }
            }
            catch (Exception ex)
            {
                if (running)
                {
                    OnError?.Invoke($"Serial read failed: {ex.Message}");
                    MarkConnectionLost(port);
                }
            }
        }

        private void MarkConnectionLost(SerialPort failedPort)
        {
            lock (connectionLock)
            {
                if (!ReferenceEquals(serialPort, failedPort))
                {
                    return;
                }

                running = false;
                serialPort = null;
                readThread = null;
            }

            try
            {
                failedPort.Dispose();
            }
            catch
            {
            }
        }

        private void ProcessIncoming(byte[] incoming, int count)
        {
            List<string> errors = new List<string>();
            List<MSPPacket> packets;

            lock (receiveLock)
            {
                for (int i = 0; i < count; i++)
                {
                    receiveBuffer.Add(incoming[i]);
                }

                packets = ExtractPackets(receiveBuffer, errors);
            }

            foreach (string error in errors)
            {
                OnError?.Invoke(error);
            }

            foreach (MSPPacket packet in packets)
            {
                ProcessPacket(packet);
            }
        }

        private static List<MSPPacket> ExtractPackets(List<byte> buffer, List<string> errors)
        {
            List<MSPPacket> packets = new List<MSPPacket>();

            while (buffer.Count >= 2)
            {
                int headerIndex = FindHeader(buffer);
                if (headerIndex < 0)
                {
                    bool keepDollar = buffer[buffer.Count - 1] == HeaderDollar;
                    buffer.Clear();
                    if (keepDollar)
                    {
                        buffer.Add(HeaderDollar);
                    }
                    break;
                }

                if (headerIndex > 0)
                {
                    buffer.RemoveRange(0, headerIndex);
                }

                if (buffer.Count < HeaderLength)
                {
                    break;
                }

                byte type = buffer[2];
                if (type != PacketCommand && type != PacketResponse)
                {
                    buffer.RemoveAt(0);
                    continue;
                }

                ushort function = (ushort)(buffer[4] | (buffer[5] << 8));
                ushort payloadLength = (ushort)(buffer[6] | (buffer[7] << 8));
                if (payloadLength > MaxPayloadLength)
                {
                    errors?.Add($"Ignoring MSP packet with invalid payload length {payloadLength}");
                    buffer.RemoveAt(0);
                    continue;
                }

                int packetLength = HeaderLength + payloadLength + 1;
                if (buffer.Count < packetLength)
                {
                    break;
                }

                byte expectedCRC = CalculateCRC(buffer, 3, HeaderLength - 3 + payloadLength);
                byte receivedCRC = buffer[packetLength - 1];
                if (expectedCRC != receivedCRC)
                {
                    errors?.Add($"Ignoring MSP packet with invalid CRC (expected 0x{expectedCRC:X2}, received 0x{receivedCRC:X2})");
                    buffer.RemoveAt(0);
                    continue;
                }

                byte[] payload = new byte[payloadLength];
                if (payloadLength > 0)
                {
                    buffer.CopyTo(HeaderLength, payload, 0, payloadLength);
                }

                packets.Add(new MSPPacket(type, function, payload));
                buffer.RemoveRange(0, packetLength);
            }

            return packets;
        }

        private static int FindHeader(List<byte> buffer)
        {
            for (int i = 0; i < buffer.Count - 1; i++)
            {
                if (buffer[i] == HeaderDollar && buffer[i + 1] == HeaderX)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ProcessPacket(MSPPacket packet)
        {
            if (packet.Type == PacketCommand && packet.Function == SetRecordingState && packet.Payload.Length > 0)
            {
                if (packet.Payload[0] == RecordingStarted)
                {
                    OnStartRaceCommand?.Invoke();
                }
                else if (packet.Payload[0] == RecordingStopped)
                {
                    OnStopRaceCommand?.Invoke();
                }
            }
            else if (packet.Type == PacketResponse && packet.Function == GetBackpackVersion)
            {
                OnBackpackVersion?.Invoke(DecodeVersion(packet.Payload));
            }
        }

        private static string DecodeVersion(byte[] payload)
        {
            int length = Array.IndexOf(payload, (byte)0);
            if (length < 0)
            {
                length = payload.Length;
            }

            return Encoding.UTF8.GetString(payload, 0, length);
        }

        private static byte[] BuildPacket(byte type, ushort function, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            byte[] packet = new byte[HeaderLength + payload.Length + 1];

            packet[0] = HeaderDollar;
            packet[1] = HeaderX;
            packet[2] = type;
            packet[3] = 0;
            packet[4] = (byte)(function & 0xFF);
            packet[5] = (byte)(function >> 8);
            packet[6] = (byte)(payload.Length & 0xFF);
            packet[7] = (byte)(payload.Length >> 8);
            Array.Copy(payload, 0, packet, HeaderLength, payload.Length);
            packet[packet.Length - 1] = CalculateCRC(packet, 3, HeaderLength - 3 + payload.Length);

            return packet;
        }

        private static byte CalculateCRC(IList<byte> data, int start, int length)
        {
            byte crc = 0;
            for (int i = start; i < start + length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x80) != 0
                        ? (byte)((crc << 1) ^ 0xD5)
                        : (byte)(crc << 1);
                }
            }

            return crc;
        }

        public void Dispose()
        {
            Disconnect();
        }

        private sealed class MSPPacket
        {
            public byte Type { get; }
            public ushort Function { get; }
            public byte[] Payload { get; }

            public MSPPacket(byte type, ushort function, byte[] payload)
            {
                Type = type;
                Function = function;
                Payload = payload;
            }
        }
    }
}
