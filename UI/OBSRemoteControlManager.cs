using Composition.Nodes;
using ExternalData;
using RaceLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Timing;
using Tools;
using UI.Nodes;

namespace UI
{
    public class OBSRemoteControlManager : IDisposable
    {
        public enum Triggers
        {

            ClickStartRace,
            StartRaceTone,
            ClickEndRace,
           
            PreRaceTab,
            PostRaceTab,

            LiveTab,
            RoundsTab,
            ReplayTab,
            StatsTab
        }

        private OBSRemoteControlConfig config;
        private OBSRemoteControl remoteControl;

        private SceneManagerNode sceneManagerNode;
        private TabbedMultiNode tabbedMultiNode;
        private EventManager eventManager;

        private bool eventsHooked;

        public event Action<bool> Activity;

        public bool Connected 
        { 
            get
            {
                if (remoteControl == null)
                    return false;
                return remoteControl.Connected;
            }
        }

        public bool Enabled
        {
            get
            {
                return config.Enabled;
            }

        }

        public OBSRemoteControlManager(SceneManagerNode sceneManagerNode, TabbedMultiNode tabbedMultiNode, EventManager eventManager)
        {
            this.sceneManagerNode = sceneManagerNode;
            this.tabbedMultiNode = tabbedMultiNode;
            this.eventManager = eventManager;

            config = OBSRemoteControlConfig.Load();

            if (config.Enabled) 
            {
                sceneManagerNode.OnSceneChange += OnSceneChange;
                eventManager.RaceManager.OnRaceStart += OnRaceStart;
                eventManager.RaceManager.OnRacePreStart += OnRacePreStart;
                tabbedMultiNode.OnTabChange += OnTabChange;
                eventsHooked = true;

                remoteControl = new OBSRemoteControl(config.Host, config.Port, config.Password);
                remoteControl.Activity += RemoteControl_Activity;
                remoteControl.Connect();
            }
        }

        private void RemoteControl_Activity(bool success)
        {
            Activity?.Invoke(success);
        }

        public void Dispose()
        {
            remoteControl?.Dispose();

            if (eventsHooked)
            {
                sceneManagerNode.OnSceneChange -= OnSceneChange;
                eventManager.RaceManager.OnRaceStart -= OnRaceStart;
                eventManager.RaceManager.OnRacePreStart -= OnRacePreStart;
                tabbedMultiNode.OnTabChange -= OnTabChange;
            }
        }

        public void Trigger(Triggers type)
        {
            if (remoteControl == null)
                return;

            foreach (OBSRemoteControlEvent rcEvent in config.RemoteControlEvents)
            {
                if (rcEvent.Trigger == type) 
                {
                    Logger.OBS.LogCall(this, rcEvent.GetType().Name, rcEvent.ToString());
                    
                    if (rcEvent is OBSRemoteControlSetSceneEvent)
                    {
                        OBSRemoteControlSetSceneEvent a = rcEvent as OBSRemoteControlSetSceneEvent;
                        remoteControl.SetScene(a.SceneName);
                    }

                    if (rcEvent is OBSRemoteControlSourceFilterToggleEvent)
                    {
                        OBSRemoteControlSourceFilterToggleEvent a = rcEvent as OBSRemoteControlSourceFilterToggleEvent;
                        remoteControl.SetSourceFilterEnabled(a.SourceName, a.FilterName, a.Enable);
                    }
                }
            }
        }

        private void OnTabChange(string tab, Node node)
        {
            switch (tab)
            {
                case "Live":
                    Trigger(Triggers.LiveTab);
                    break;

                case "Replay":
                    Trigger(Triggers.ReplayTab);
                    break;

                case "Rounds":
                    Trigger(Triggers.RoundsTab);
                    break;

                default:
                    Trigger(Triggers.StatsTab);
                    break;
            }
        }

        private void OnRacePreStart(Race race)
        {
            Trigger(Triggers.ClickStartRace);
        }

        private void OnRaceStart(Race race)
        {
            Trigger(Triggers.StartRaceTone);
        }

        private void OnSceneChange(SceneManagerNode.Scenes scene)
        {
            switch (scene) 
            {
                default:
                    Trigger(Triggers.LiveTab);
                    break;

                case SceneManagerNode.Scenes.PreRace:
                    Trigger(Triggers.PreRaceTab);
                    break;

                case SceneManagerNode.Scenes.RaceResults:
                    Trigger(Triggers.PostRaceTab);
                    break;
            }
        }

        public abstract class OBSRemoteControlEvent
        {
            public Triggers Trigger { get; set; }
        }

        public class OBSRemoteControlSetSceneEvent : OBSRemoteControlEvent
        {
            public string SceneName { get; set; }

            public override string ToString()
            {
                return Trigger + " -> " + SceneName;
            }
        }

        public class OBSRemoteControlSourceFilterToggleEvent: OBSRemoteControlEvent
        {
            public string SourceName { get; set; }
            public string FilterName { get; set; }
            public bool Enable { get; set; }

            public override string ToString()
            {
                return Trigger + " -> " + SourceName + " " + FilterName + " " + Enable;
            }
        }

        [XmlInclude(typeof(OBSRemoteControlEvent)),
         XmlInclude(typeof(OBSRemoteControlSetSceneEvent)),
         XmlInclude(typeof(OBSRemoteControlSourceFilterToggleEvent))]

        public class OBSRemoteControlConfig
        {
            public bool Enabled { get; set; }

            public string Host { get; set; }

            public int Port { get; set; }

            public string Password { get; set; }

            [Browsable(false)]
            public List<OBSRemoteControlEvent> RemoteControlEvents { get; set; }

            public OBSRemoteControlConfig()
            {
                Enabled = false;
                Host = "localhost";
                Port = 4455;
                Password = "42ZzDvzK3Cd43HQW";

                RemoteControlEvents = new List<OBSRemoteControlEvent>();
            }

            protected const string filename = @"data/OBSRemoteControl.xml";
            public static OBSRemoteControlConfig Load()
            {
                OBSRemoteControlConfig config = new OBSRemoteControlConfig();

                bool error = false;
                try
                {
                    OBSRemoteControlConfig[] s = IOTools.Read<OBSRemoteControlConfig>(filename);

                    if (s != null && s.Any())
                    {
                        config = s[0];
                        Write(config);
                    }
                    else
                    {
                        error = true;
                    }
                }
                catch
                {
                    error = true;
                }

                if (error)
                {
                    OBSRemoteControlConfig s = new OBSRemoteControlConfig();
                    Write(s);
                    config = s;
                }

                return config;
            }

            public static void Write(OBSRemoteControlConfig s)
            {
                IOTools.Write(filename, s);
            }

            public override string ToString()
            {
                return "OBS Remote Control Config";
            }
        }
    }
}
