using SocketIOClient;
using SocketIOClient.Arguments;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Timing.RotorHazard
{
    internal class SocketIOHeartbeat : IDisposable
    {
        public bool Connected { get; private set; }

        private Thread heartBeat;
        private bool runBeat;
        private DateTime lastHeartBeat;

        private TimeSpan waitTimeout;

        private object locker;

        private SocketIO socket;

        private ITimingSystem owner;

        public delegate void Response(string text);

        public event Action OnConnected;
        public event Response OnHeartBeat;

        public SocketIOHeartbeat(ITimingSystem owner)
        {
            this.owner = owner;

            waitTimeout = TimeSpan.FromSeconds(5);

            locker = new object();
        }

        public void Dispose()
        {
            Disconnect();
        }

        public bool Connect(string host)
        {
            try
            {
                if (socket != null)
                {
                    socket.CloseAsync();
                    socket = null;
                }

                socket = new SocketIO(host);
                socket.KeepAliveInterval = TimeSpan.Zero;
                socket.OnConnected += SuccessfulConnection;
                socket.OnClosed += Closed;
                socket.UnhandledEvent += UnhandledEvent;
                socket.OnError += OnError;
                socket.On("heartbeat", HeartBeat);

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
            catch (Exception e)
            {
                Logger.TimingLog.LogException(owner, e);
                return false;
            }
        }


        public bool Disconnect()
        {
            try
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

                return true;
            }
            catch (Exception e)
            {
                Logger.TimingLog.LogException(owner, e);
                return false;
            }
        }

        private void SuccessfulConnection()
        {
            Connected = true;
            Logger.TimingLog.Log(owner, "Connected");

            if (heartBeat != null)
            {
                runBeat = false;
                heartBeat.Join();
                heartBeat = null;
            }
                
            runBeat = true;

            heartBeat = new Thread(ClientHeartBeat);
            heartBeat.Name = owner.GetType().Name + " heartbeat";
            heartBeat.Start();

            OnConnected?.Invoke();
        }

        private void UnhandledEvent(string arg1, ResponseArgs arg2)
        {
            Logger.TimingLog.Log(owner, "Unhandled Event", arg1 + " " + arg2.Text);
        }

        private void OnError(ResponseArgs obj)
        {
            Logger.TimingLog.Log(owner, obj.ToString());
        }

        private void Closed(ServerCloseReason obj)
        {
            Disconnect();
            Logger.TimingLog.Log(owner, "Disconnected", obj);
        }

        private void ClientHeartBeat()
        {
            while (runBeat)
            {
                Thread.Sleep(1000);
                try
                {
                    lock (locker)
                    {
                        SocketIO socket = this.socket;
                        if (socket != null)
                        {
                            if (!socket.PingAsync().Wait(waitTimeout))
                            {
                                Connected = false;
                            }
                        }
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
                    Logger.TimingLog.LogException(owner, e);
                }
            }
        }

        private void HeartBeat(ResponseArgs args)
        {
            Connected = true;
            lastHeartBeat = DateTime.Now;

            OnHeartBeat?.Invoke(args.Text);
        }

        public void On(string command, Response callback)
        {
            lock (locker)
            {
                SocketIO socket = this.socket;
                if (socket != null)
                {
                    socket.On(command, (r) => { callback(r.Text); });
                }
            }
        }

        public bool Emit(string command, object obj)
        {
            lock (locker)
            {
                try
                {
                    SocketIO socket = this.socket;
                    if (socket != null)
                    {
                        if (socket.EmitAsync(command, obj).Wait(waitTimeout))
                        {
                            return true;
                        }
                        else
                        {
                            Logger.TimingLog.Log(this, "Emit Failure");
                            Connected = false;
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.TimingLog.LogException(this, ex);
                    Connected = false;
                    return false;
                }

            }
            return false;
        }

        public bool Emit(string command, Response callback)
        {
            try
            {
                lock (locker)
                {
                    SocketIO socket = this.socket;
                    if (socket != null)
                    {
                        if (socket.EmitAsync(command, (r) => { callback(r.Text); }).Wait(waitTimeout))
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

            }
            catch
            {
                Connected = false;
                return false;
            }

            return false;
        }
    }
}
