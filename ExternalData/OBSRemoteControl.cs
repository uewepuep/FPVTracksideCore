using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using RaceLib;

namespace ExternalData
{
    public class OBSRemoteControl
    {
        private OBSWebsocket obsWebsocket;

        public bool Connected { get; private set; }

        private Dictionary<Scenes, string> translate;

        public enum Scenes
        {
            PreRace,
            MidRace,
            PostRace,
            Rounds,
            Replay,
            Stats
        }

        public OBSRemoteControl() 
        {
            obsWebsocket = new OBSWebsocket();
            obsWebsocket.Connected += OnConnected;
            obsWebsocket.Disconnected += OnDisconnected;

            translate = new Dictionary<Scenes, string>();
        }

        public void Add(Scenes scene, string name)
        {
            translate.Add(scene, name);
        }

        public void Connect(string url, int port, string password)
        {
            obsWebsocket.ConnectAsync("ws://" + url + ":" + port, password);
        }

        private void OnConnected(object sender, EventArgs e)
        {
            Connected = true;
        }

        private void OnDisconnected(object sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo e)
        {
            Connected = false;
        }

        public IEnumerable<string> GetSceneList()
        {
            GetSceneListInfo sceneListInfo = obsWebsocket.GetSceneList();
            return sceneListInfo.Scenes.Select(s => s.Name);
        }

        public void SceneChange(Scenes scene)
        {
            if (!Connected)
            {
                return;
            }

            if (translate.TryGetValue(scene, out string name)) 
            { 
                obsWebsocket.SetCurrentProgramScene(name);
            }
        }
    }
}
