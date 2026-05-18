using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tools;

namespace Timing.Velocidrone
{
    /// <summary>
    /// Low-level Velocidrone websocket client. All protocol assumptions are isolated here.
    /// </summary>
    public class VelocidroneWebSocketClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private ClientWebSocket _client;
        private CancellationTokenSource _receiveCts;
        private Task _receiveTask;
        private readonly object _sendLock = new object();

        public bool IsConnected => _client?.State == WebSocketState.Open;

        public event Action<string> OnMessageReceived;
        public event Action OnDisconnected;
        public event Action<Exception> OnError;

        public VelocidroneWebSocketClient(string host, int port)
        {
            _host = host ?? "localhost";
            _port = port > 0 ? port : VelocidroneProtocol.DefaultPort;
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                _client?.Dispose();
                _client = new ClientWebSocket();

                var uri = new Uri($"ws://{_host}:{_port}{VelocidroneProtocol.EndpointPath}");
                Logger.TimingLog.Log(this, "Connecting to " + uri, Logger.LogType.Notice);
                await _client.ConnectAsync(uri, CancellationToken.None);

                if (_client.State == WebSocketState.Open)
                {
                    Logger.TimingLog.Log(this, "Connected to Velocidrone websocket", Logger.LogType.Notice);
                    StartReceiveLoop();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
                OnError?.Invoke(ex);
            }

            return false;
        }

        public bool Connect()
        {
            return ConnectAsync().GetAwaiter().GetResult();
        }

        private void StartReceiveLoop()
        {
            _receiveCts?.Cancel();
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoop(_receiveCts.Token);
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();

            try
            {
                while (_client != null && _client.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await _client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Logger.TimingLog.Log(this, "Velocidrone websocket closed by server", Logger.LogType.Notice);
                        break;
                    }

                    var segment = new ArraySegment<byte>(buffer, 0, result.Count);
                    sb.Append(Encoding.UTF8.GetString(segment));

                    if (result.EndOfMessage)
                    {
                        var msg = sb.ToString();
                        sb.Clear();
                        if (!string.IsNullOrWhiteSpace(msg))
                        {
                            try
                            {
                                OnMessageReceived?.Invoke(msg);
                            }
                            catch (Exception ex)
                            {
                                Logger.TimingLog.LogException(this, ex);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Logger.TimingLog.LogException(this, ex);
                    OnError?.Invoke(ex);
                }
            }
            finally
            {
                OnDisconnected?.Invoke();
            }
        }

        public void Send(string json)
        {
            if (_client?.State != WebSocketState.Open || string.IsNullOrEmpty(json))
                return;

            lock (_sendLock)
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    _client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                    Logger.TimingLog.Log(this, "Sent: " + json.Substring(0, Math.Min(80, json.Length)) + (json.Length > 80 ? "..." : ""), Logger.LogType.Notice);
                }
                catch (Exception ex)
                {
                    Logger.TimingLog.LogException(this, ex);
                }
            }
        }

        public void Disconnect()
        {
            _receiveCts?.Cancel();
            try
            {
                _client?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { }
            _client?.Dispose();
            _client = null;
        }

        public void Dispose()
        {
            Disconnect();
        }

        /// <summary>
        /// Try to parse a JSON message and return the root keys for routing.
        /// </summary>
        public static bool TryParseMessage(string json, out JObject obj)
        {
            obj = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                obj = JObject.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
