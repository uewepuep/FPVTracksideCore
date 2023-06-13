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

        public OBSRemoteControl(string host, int port, string password) 
        {
            this.host = host;
            this.port = port;
            this.password = password;


            obsWebsocket = new OBSWebsocket();
            obsWebsocket.Connected += OnConnected;
            obsWebsocket.Disconnected += OnDisconnected;
        }

        public void Dispose()
        {
            obsWebsocket.Disconnect();
            obsWebsocket = null;
        }

        public void Connect()
        {
            if (connecting)
                return;

            string url = "ws://" + host + ":" + port;
            Logger.OBS.LogCall(this, url, password);

            connecting = true;
            obsWebsocket.ConnectAsync(url, password);
        }

        public bool WaitConnection()
        {
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

        private void OnConnected(object sender, EventArgs e)
        {
            Logger.OBS.LogCall(this);
            Connected = true;
            connecting = false;
        }

        private void OnDisconnected(object sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo e)
        {
            Logger.OBS.LogCall(this, e, e.DisconnectReason);
            connecting = false;
            Connected = false;
        }

        public IEnumerable<string> GetSceneList()
        {
            GetSceneListInfo sceneListInfo = obsWebsocket.GetSceneList();
            return sceneListInfo.Scenes.Select(s => s.Name);
        }

        public IEnumerable<string> GetSourceFilters()
        {
            foreach (string scene in GetSceneList()) 
            {
                List<SceneItemDetails> sceneDetails = obsWebsocket.GetSceneItemList(scene);
                foreach (SceneItemDetails details in sceneDetails) 
                {
                    string source = details.SourceName;
                    List<FilterSettings> filters = obsWebsocket.GetSourceFilterList(source);
                    foreach (FilterSettings filter in filters) 
                    { 
                        yield return source + " - " + filter.Name;
                    }
                }
            }
        }

        public void SetScene(string name)
        {
            if (!Connected || obsWebsocket == null)
            {
                Connect();
                WaitConnection();
            }

            if (!Connected || obsWebsocket == null)
            {
                return;
            }


            try
            {
                obsWebsocket.SetCurrentProgramScene(name);
            }
            catch (Exception e)
            {
                Logger.OBS.LogException(this, e);
            }
        }

        public void SetSourceFilterEnabled(string source, string filter, bool enabled)
        {
            if (!Connected || obsWebsocket == null)
            {
                return;
            }

            try
            {
                obsWebsocket.SetSourceFilterEnabled(source, filter, enabled);
            }
            catch (Exception e)
            {
                Logger.OBS.LogException(this, e);
            }
        }

        public IEnumerable<string> GetOptions()
        {
            foreach (string scene in GetSceneList())
            {
                yield return "Scene: " + scene;
            }

            foreach (string scene in GetSourceFilters())
            {
                yield return "Source Filter Toggle: " + scene;
            }
        }
    }
}
