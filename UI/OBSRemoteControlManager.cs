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
            RaceEnd,
            TimesUp,

            RaceStartCancelled,
           
            PreRaceScene,
            PostRaceScene,
            LiveScene,


            LiveTab,
            RoundsTab,
            ReplayTab,

            LapRecordsTab,
            LapCountTab,
            PointsTab,
            ChannelListTab,
            RSSITab,

            PhotoBoothTab,
            PatreonsTab,

            ChannelGrid1,
            ChannelGrid2,
            ChannelGrid3,
            ChannelGrid4,
            ChannelGrid5,
            ChannelGrid6,
            ChannelGrid7,
            ChannelGrid8,

        }

        private OBSRemoteControlConfig config;
        private OBSRemoteControl remoteControl;

        private SceneManagerNode sceneManagerNode;
        private TracksideTabbedMultiNode tabbedMultiNode;
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

        public IEnumerable<Triggers> ChannelGrids
        {
            get
            {
                yield return Triggers.ChannelGrid1;
                yield return Triggers.ChannelGrid2;
                yield return Triggers.ChannelGrid3;
                yield return Triggers.ChannelGrid4;
                yield return Triggers.ChannelGrid5;
                yield return Triggers.ChannelGrid6;
                yield return Triggers.ChannelGrid7;
                yield return Triggers.ChannelGrid8;
            }
        }

        private TimeSpan doubleTriggerTimeout;

        private Triggers lastTrigger;
        private DateTime lastTriggerTime;

        public OBSRemoteControlManager(SceneManagerNode sceneManagerNode, TracksideTabbedMultiNode tabbedMultiNode, EventManager eventManager)
        {
            doubleTriggerTimeout = TimeSpan.FromSeconds(5);

            this.sceneManagerNode = sceneManagerNode;
            this.tabbedMultiNode = tabbedMultiNode;
            this.eventManager = eventManager;

            config = OBSRemoteControlConfig.Load(eventManager.Profile);

            if (config.Enabled) 
            {
                sceneManagerNode.OnSceneChange += OnSceneChange;
                eventManager.RaceManager.OnRaceStart += OnRaceStart;
                eventManager.RaceManager.OnRacePreStart += OnRacePreStart;
                eventManager.RaceManager.OnRaceEnd += RaceManager_OnRaceEnd;
                eventManager.RaceManager.OnRaceCancelled += RaceManager_OnRaceCancelled;
                eventManager.RaceManager.OnRaceResumed += OnRaceStart;
                tabbedMultiNode.OnTabChange += OnTabChange;
                sceneManagerNode.ChannelsGridNode.OnGridCountChanged += OnGridCountChanged;

                eventsHooked = true;

                remoteControl = new OBSRemoteControl(config.Host, config.Port, config.Password);
                remoteControl.Activity += RemoteControl_Activity;
                remoteControl.Connect();
            }
        }

        private void OnGridCountChanged(int count)
        {
            Triggers[] triggers = ChannelGrids.ToArray();

            int index = count - 1;

            if (index >= 0 && index < triggers.Length)
            {
                Trigger(triggers[index]);
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
                eventManager.RaceManager.OnRaceResumed -= OnRaceStart;
                eventManager.RaceManager.OnRacePreStart -= OnRacePreStart;
                eventManager.RaceManager.OnRaceEnd -= RaceManager_OnRaceEnd;
                tabbedMultiNode.OnTabChange -= OnTabChange;
                sceneManagerNode.ChannelsGridNode.OnGridCountChanged -= OnGridCountChanged;
            }
        }

        public void Trigger(Triggers type)
        {
            if (remoteControl == null)
                return;

            if (type == lastTrigger && lastTriggerTime + doubleTriggerTimeout > DateTime.Now)
                return;

            lastTrigger = type;
            lastTriggerTime = DateTime.Now;

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
            if (tabbedMultiNode.IsOnLive) Trigger(Triggers.LiveTab);
            if (tabbedMultiNode.IsOnRounds) Trigger(Triggers.RoundsTab);
            if (tabbedMultiNode.IsOnChanelList) Trigger(Triggers.ChannelListTab);
            if (tabbedMultiNode.IsOnLapRecords) Trigger(Triggers.LapRecordsTab);
            if (tabbedMultiNode.IsOnLapCount) Trigger(Triggers.LapCountTab);
            if (tabbedMultiNode.IsOnRSSI) Trigger(Triggers.RSSITab);
            if (tabbedMultiNode.IsOnChanelList) Trigger(Triggers.ChannelListTab);
            if (tabbedMultiNode.IsOnPhotoBooth) Trigger(Triggers.PhotoBoothTab);
            if (tabbedMultiNode.IsOnPatreons) Trigger(Triggers.PatreonsTab);
            if (tabbedMultiNode.IsOnPoints) Trigger(Triggers.PointsTab);
            if (tabbedMultiNode.IsOnReplay) Trigger(Triggers.ReplayTab);
        }

        private void OnRacePreStart(Race race)
        {
            Trigger(Triggers.ClickStartRace);
        }

        private void OnRaceStart(Race race)
        {
            Trigger(Triggers.StartRaceTone);
        }

        private void RaceManager_OnRaceEnd(Race race)
        {
            Trigger(Triggers.RaceEnd);
        }

        private void RaceManager_OnRaceCancelled(Race arg1, bool arg2)
        {
            Trigger(Triggers.RaceStartCancelled);
        }

        private void OnSceneChange(SceneManagerNode.Scenes scene)
        {
            switch (scene) 
            {
                default:
                    Trigger(Triggers.LiveScene);
                    break;

                case SceneManagerNode.Scenes.PreRace:
                    Trigger(Triggers.PreRaceScene);
                    break;

                case SceneManagerNode.Scenes.RaceResults:
                    Trigger(Triggers.PostRaceScene);
                    break;
            }
        }


        [XmlInclude(typeof(OBSRemoteControlEvent)),
         XmlInclude(typeof(OBSRemoteControlSetSceneEvent)),
         XmlInclude(typeof(OBSRemoteControlSourceFilterToggleEvent))]

        public class OBSRemoteControlConfig
        {
            public bool Enabled { get; set; }

            [Category("Connection")]
            public string Host { get; set; }
            [Category("Connection")]

            public int Port { get; set; }
            [Category("Connection")]

            public string Password { get; set; }

            [Browsable(false)]
            public List<OBSRemoteControlEvent> RemoteControlEvents { get; set; }

            public OBSRemoteControlConfig()
            {
                Enabled = false;
                Host = "localhost";
                Port = 4455;
#if DEBUG
                Password = "42ZzDvzK3Cd43HQW";
#endif
                RemoteControlEvents = new List<OBSRemoteControlEvent>();
            }

            protected const string filename = "OBSRemoteControl.xml";
            public static OBSRemoteControlConfig Load(Profile profile)
            {
                OBSRemoteControlConfig config = new OBSRemoteControlConfig();

                bool error = false;
                try
                {
                    OBSRemoteControlConfig[] s = IOTools.Read<OBSRemoteControlConfig>(profile, filename);

                    if (s != null && s.Any())
                    {
                        config = s[0];
                        Write(profile, config);
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
                    Write(profile, s);
                    config = s;
                }

                return config;
            }

            public static void Write(Profile profile, OBSRemoteControlConfig s)
            {
                IOTools.Write(profile, filename, s);
            }

            public override string ToString()
            {
                return "OBS Remote Control Config";
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

        public class OBSRemoteControlSourceFilterToggleEvent : OBSRemoteControlEvent
        {
            public string SourceName { get; set; }
            public string FilterName { get; set; }
            public bool Enable { get; set; }

            public override string ToString()
            {
                return Trigger + " -> " + SourceName + " " + FilterName + " " + Enable;
            }
        }
    }
}
