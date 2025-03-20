using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tools;

namespace ExternalData
{
    public class OBSRemoteControl : IDisposable
    {
        private OBSWebsocket obsWebsocket;

        public bool Connected { get; private set; }

        private bool connecting;


        private string host;
        private int port;
        private string password;

        private WorkQueue workQueue;

        public event Action<bool> Activity;

        public OBSRemoteControl(string host, int port, string password) 
        {
            this.host = host;
            this.port = port;
            this.password = password;

            obsWebsocket = new OBSWebsocket();
            workQueue = new WorkQueue("OBSRemoteControl");

            obsWebsocket.Connected += OnConnected;
            obsWebsocket.Disconnected += OnDisconnected;
        }

        public void Dispose()
        {
            obsWebsocket?.Disconnect();
            obsWebsocket = null;

            workQueue?.Dispose();
            workQueue = null;
        }

        public bool Connect()
        {
            try
            {
                if (connecting)
                    return false;

                string url = "ws://" + host + ":" + port;
                Logger.OBS.LogCall(this, url, password);

                connecting = true;
                obsWebsocket.ConnectAsync(url, password);
                Activity?.Invoke(true);
                return true;
            }
            catch (Exception e)
            {
                Logger.OBS.LogException(this, e);
                return false;
            }
        }

        public bool ConnectWait()
        {
            try
            {
                if (Connected)
                return true;

                if (!connecting)
                    Connect();

                OBSWebsocket oBSWebsocket = obsWebsocket;
                if (oBSWebsocket == null)
                    return false;

                DateTime start = DateTime.Now;
                while (connecting)
                {
                    Thread.Sleep(100);
                    if (DateTime.Now - start > obsWebsocket.WSTimeout)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.OBS.LogException(this, e);
                return false;
            }
        }

        private void OnConnected(object sender, EventArgs e)
        {
            Logger.OBS.LogCall(this);
            Connected = true;
            connecting = false;

            Activity?.Invoke(true);
        }

        private void OnDisconnected(object sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo e)
        {
            Logger.OBS.LogCall(this, e, e.DisconnectReason);
            connecting = false;
            Connected = false;

            Activity?.Invoke(true);
        }

        private delegate IEnumerable<string> stringReturner();

        private void callBackIenumerable(stringReturner input, Action<string[]> callback)
        {
            workQueue?.Enqueue(() =>
            {
                try
                {
                    string[] strings = input().ToArray();
                   
                    Logger.OBS.Log(this, "", strings);

                    callback(strings);
                    Activity?.Invoke(true);
                }
                catch (Exception ex) 
                {
                    Logger.OBS.LogException(this, ex);
                    Activity?.Invoke(false);
                }
            });
        }


        public void GetScenes(Action<string[]> callback)
        {
            callBackIenumerable(GetScenes, callback);
        }

        private IEnumerable<string> GetScenes()
        {
            ConnectWait();

            GetSceneListInfo sceneListInfo = obsWebsocket.GetSceneList();
            return sceneListInfo.Scenes.Select(s => s.Name);
        }

        public void GetSources(Action<string[]> callback)
        {
            callBackIenumerable(GetSources, callback);
        }

        private IEnumerable<string> GetSources()
        {
            ConnectWait();

            foreach (string scene in GetScenes()) 
            {
                List<SceneItemDetails> sceneDetails = obsWebsocket.GetSceneItemList(scene);
                foreach (SceneItemDetails details in sceneDetails) 
                {
                    string source = details.SourceName;
                    yield return source;
                }
            }
        }

        public void GetFilters(Action<string[]> callback)
        {
            callBackIenumerable(GetFilters, callback);
        }
        private IEnumerable<string> GetFilters()
        {
            ConnectWait();

            foreach (string scene in GetScenes())
            {
                List<SceneItemDetails> sceneDetails = obsWebsocket.GetSceneItemList(scene);
                foreach (SceneItemDetails details in sceneDetails)
                {
                    string source = details.SourceName;
                    List<FilterSettings> filters = obsWebsocket.GetSourceFilterList(source);
                    foreach (FilterSettings filter in filters)
                    {
                        yield return filter.Name;
                    }
                }
            }
        }

        public void GetHotKeys(Action<string[]> callback)
        {
            callBackIenumerable(GetHotKeys, callback);
        }

        private IEnumerable<string> GetHotKeys()
        {
            ConnectWait();
            return obsWebsocket.GetHotkeyList().Distinct().OrderBy(r => r);
        }

        public void SetScene(string name)
        {
            workQueue?.Enqueue(() =>
            {
                if (!Connected || obsWebsocket == null)
                {
                    Connect();
                    ConnectWait();
                }

                if (!Connected || obsWebsocket == null)
                {
                    return;
                }


                try
                {
                    obsWebsocket.SetCurrentProgramScene(name);
                    Activity?.Invoke(true);
                }
                catch (Exception e)
                {
                    Logger.OBS.LogException(this, e);
                    Activity?.Invoke(false);
                }
            });
        }

        public void SetSourceFilterEnabled(string source, string filter, bool enabled)
        {
            workQueue?.Enqueue(() =>
            {
                if (!Connected || obsWebsocket == null)
                {
                    return;
                }

                try
                {
                    obsWebsocket.SetSourceFilterEnabled(source, filter, enabled);
                    Activity?.Invoke(true);
                }
                catch (Exception e)
                {
                    Logger.OBS.LogException(this, e);
                    Activity?.Invoke(false);
                }

            });
        }

        public void TriggerHotKeyAction(string hotkeyactionaname)
        {
            workQueue?.Enqueue(() =>
            {
                if (!Connected || obsWebsocket == null)
                {
                    return;
                }

                try
                {
                    obsWebsocket.TriggerHotkeyByName(hotkeyactionaname);
                    Activity?.Invoke(true);
                }
                catch (Exception e)
                {
                    Logger.OBS.LogException(this, e);
                    Activity?.Invoke(false);
                }

            });
        }

        public void TriggerHotKeySequence(OBSHotkey hotKey, KeyModifier modifiers)
        {
            workQueue?.Enqueue(() =>
            {
                if (!Connected || obsWebsocket == null)
                {
                    return;
                }

                try
                {
                    obsWebsocket.TriggerHotkeyByKeySequence(hotKey, modifiers);
                    Activity?.Invoke(true);
                }
                catch (Exception e)
                {
                    Logger.OBS.LogException(this, e);
                    Activity?.Invoke(false);
                }

            });
        }
    }
}
