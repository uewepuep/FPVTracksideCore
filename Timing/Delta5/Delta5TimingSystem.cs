using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SocketIOClient;
using SocketIOClient.Arguments;
using Tools;

namespace Timing.Delta5
{
    public class Delta5TimingSystem : ITimingSystem
    {
        public virtual TimingSystemType Type { get { return TimingSystemType.Delta5; } }

        public bool Connected { get; private set; }

        private Delta5TimingSettings settings;
        public TimingSystemSettings Settings { get { return settings; } set { settings = value as Delta5TimingSettings; } }

        public event DetectionEventDelegate OnDetectionEvent;

        private SocketIO socket;

        private DateTime epoch;

        private Thread heartBeat;
        private bool runBeat;
        private DateTime lastHeartBeat;

        private TimeSpan waitTimeout;

        private object locker;

        public int MaxPilots { get { return 5; } }

        public IEnumerable<StatusItem> Status { get { return new StatusItem[0]; } }

        public Delta5TimingSystem()
        {
            settings = new Delta5TimingSettings();
            epoch = DateTime.Now;

            waitTimeout = TimeSpan.FromSeconds(5);

            locker = new object();
        }

        private void ClientHeartBeat()
        {
            while(runBeat)
            {
                Thread.Sleep(1000);
                try
                {
                    if (!socket.PingAsync().Wait(waitTimeout))
                    {
                        Connected = false;
                    }

                    if ((DateTime.Now - lastHeartBeat).TotalSeconds > 10)
                    {
                        Connected = false;
                    }
                }
                catch (Exception e)
                {
                    runBeat = false;
                    Connected = false;
                    Logger.TimingLog.LogException(this, e);
                }
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        public virtual bool Connect()
        {
            try
            {
                lock (locker)
                {
                    if (socket != null)
                    {
                        socket.CloseAsync();
                        socket = null;
                    }


                    socket = new SocketIO("http://" + settings.HostName + ":" + settings.Port);
                    socket.KeepAliveInterval = TimeSpan.Zero;
                    socket.OnConnected += OnConnected;
                    socket.OnClosed += Closed;
                    socket.UnhandledEvent += Socket_UnhandledEvent;
                    socket.On("heartbeat", OnHeartBeat);
                    socket.On("pass_record", OnPassRecord);
                    socket.On("frequency_set", (a) => { });
                    socket.OnError += OnError;

                    if (socket.ConnectAsync().Wait(waitTimeout))
                    {
                        Connected = true;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.TimingLog.LogException(this, e);
                return false;
            }
        }

        private void Socket_UnhandledEvent(string arg1, ResponseArgs arg2)
        {
            Logger.TimingLog.Log(this, "Unhandled Event", arg1 + " " + arg2.Text);
        }

        private void OnError(ResponseArgs obj)
        {
            Logger.TimingLog.Log(this, obj.ToString());
        }

        private void OnHeartBeat(SocketIOClient.Arguments.ResponseArgs args)
        {
            Connected = true;
            lastHeartBeat = DateTime.Now;
        }

        private void OnPassRecord(SocketIOClient.Arguments.ResponseArgs args)
        {
            PassRecord response = JsonConvert.DeserializeObject<PassRecord>(args.Text);
            Logger.TimingLog.Log(this, "PassRecord", args.Text);

            DateTime time = epoch.AddMilliseconds(response.timestamp);

            OnDetectionEvent?.Invoke(this, response.frequency, time, (int)response.peak_rssi);
        }

        private void GotTimestamp(ResponseArgs args)
        {
            TimeStamp response = JsonConvert.DeserializeObject<TimeStamp>(args.Text);
            epoch = DateTime.Now.AddMilliseconds(-response.timestamp);

            Logger.TimingLog.Log(this, "Timestamp", response.timestamp.ToString("N"));
            Logger.TimingLog.Log(this, "Epoch", epoch.ToLongTimeString());
        }
        
        private void Closed(ServerCloseReason obj)
        {
            Disconnect();
            Logger.TimingLog.Log(this, "Disconnected", obj);
        }

        protected virtual void OnConnected()
        {
            lock (locker)
            {
                Connected = true;
                Logger.TimingLog.Log(this, "Connected");

                if (heartBeat != null)
                {
                    runBeat = false;
                    heartBeat.Join();
                    heartBeat = null;
                }

                heartBeat = new Thread(ClientHeartBeat);
                heartBeat.Name = "Delta 5 Heartbeat";
                heartBeat.Start();
                runBeat = true;
            }
        }

        private void SettingSet(ResponseArgs args)
        {
            if (!string.IsNullOrEmpty(args.Text))
                Logger.TimingLog.Log(this, "Setting response " + args.Text);
        }

        public bool Disconnect()
        {
            try
            {
                lock (locker)
                {
                    runBeat = false;

                    if (heartBeat != null)
                    {
                        heartBeat.Join();
                        heartBeat = null;
                    }

                    if (socket != null)
                    {
                        socket.CloseAsync();
                        socket = null;
                    }

                    Connected = false;
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.TimingLog.LogException(this, e);
                return false;
            }
        }

        protected void On(string command, SocketIOClient.EventHandler callback)
        {
            lock (locker)
            {
                SocketIO socket = this.socket;
                if (socket != null)
                {
                    socket.On(command, callback);
                }
            }
        }

        protected bool Emit(string command, SocketIOClient.EventHandler callback)
        {
            lock (locker)
            {
                SocketIO socket = this.socket;
                if (socket != null)
                {
                    if (socket.EmitAsync(command, callback).Wait(waitTimeout))
                    {
                        return true;
                    }
                    else
                    {
                        Connected = false;
                        return false;
                    }
                }
            }
            return false;
        }


        public bool StartDetection()
        {
            return Emit("get_timestamp", GotTimestamp);
        }

        public bool EndDetection()
        {
            //Delta 5 seems to always detect?
            return true;
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    List<Task> tasks = new List<Task>();

                    SetSettings setSettings = new SetSettings()
                    {
                        calibration_offset = settings.CalibrationOffset,
                        calibration_threshold = settings.CalibrationThreshold,
                        trigger_threshold = settings.TriggerThreshold,
                        filter_ratio = settings.FilterRatio
                    };

                    tasks.Add(socket.EmitAsync("set_calibration_threshold", setSettings, SettingSet));
                    tasks.Add(socket.EmitAsync("set_calibration_offset", setSettings, SettingSet));
                    tasks.Add(socket.EmitAsync("set_trigger_threshold", setSettings, SettingSet));
                    tasks.Add(socket.EmitAsync("set_filter_ratio", setSettings, SettingSet));
                    Logger.TimingLog.Log(this, "Settings Set Triggers");


                    Logger.TimingLog.Log(this, "SetListeningFrequencies", string.Join(",", newFrequencies.Select(f => f.ToString())));
                    int node = 0;
                    foreach (ListeningFrequency freqSense in newFrequencies)
                    {
                        SetFrequency sf = new SetFrequency() { node = node, frequency = freqSense.Frequency };

                        Task task = socket.EmitAsync("set_frequency", sf);
                        tasks.Add(task);
                        node++;
                    }

                    lock (locker)
                    {
                        Task t = Task.WhenAll(tasks);
                        return t.Wait(waitTimeout);
                    }
                }
                catch (Exception e)
                {
                    Logger.TimingLog.LogException(this, e);
                }

                Connected = false;
                Thread.Sleep(100);
            }

            return false;
        }

        public bool GetVoltage(out float voltage)
        {
            voltage = 0.0f;
            return true;
        }

        private class TimeStamp
        {
            public float timestamp { get; set; }
        }

        private class SetFrequency
        {
            public int node { get; set; }
            public int frequency { get; set; }
        }

        private class PassRecord
        {
            public int node { get; set; }
            public int frequency { get; set; }
            public float timestamp { get; set; }
            public float trigger_rssi { get; set; }
            public float peak_rssi_raw { get; set; }
            public float peak_rssi { get; set; }
        }

        private class SetSettings
        {
            public int calibration_threshold { get; set; }
            public int calibration_offset { get; set; }
            public int trigger_threshold { get; set; }
            public int filter_ratio { get; set; }
        }
    }

    public class Delta5TimingSettings : TimingSystemSettings
    {
        [Category("Network")]
        public string HostName { get; set; }
        [Category("Network")]
        public int Port { get; set; }


        [Category("Trigger Settings")]
        public int CalibrationThreshold { get; set; }
        [Category("Trigger Settings")]
        public int CalibrationOffset { get; set; }
        [Category("Trigger Settings")]
        public int TriggerThreshold { get; set; }
        [Category("Trigger Settings")]
        public int FilterRatio { get; set; }

        public Delta5TimingSettings()
        {
            HostName = "10.1.1.207";
            Port = 5000;

            CalibrationThreshold = 95;
            CalibrationOffset = 8;
            TriggerThreshold = 40;
            FilterRatio = 10;
        }

    }

    
}

