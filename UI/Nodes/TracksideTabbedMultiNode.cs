using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Video;

namespace UI.Nodes
{
    public class TracksideTabbedMultiNode : TabbedMultiNode
    {
        public bool IsOnLive { get { return sceneManagerNode == Showing; } }
        public bool IsOnRounds { get { return rounds == Showing; } }

        private RoundsNode rounds;
        private SceneManagerNode sceneManagerNode;
        private LapRecordsSummaryNode pbListNode;
        private PilotChanelList pilotChanelList;
        private ReplayNode replayNode;
        private PatreonsNode patreonsNode;
        private PointsSummaryNode pilotPointsNode;
        private LapCountSummaryNode pilotLapsListNode;
        private RSSIAnalyserNode rssiNode;

        private TextButtonNode replayButton;
        private TextButtonNode liveButton;

        private EventManager eventManager;
        private VideoManager VideoManager;
        
        private TextButtonNode rssiButton;



        public TracksideTabbedMultiNode(EventManager eventManager, VideoManager videoManager, RoundsNode rounds, SceneManagerNode sceneManagerContent)
            : base(TimeSpan.FromSeconds(0.6f), Theme.Current.Panel.XNA, Theme.Current.PanelAlt.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA)
        {
            this.eventManager = eventManager;
            this.VideoManager = videoManager;

            this.rounds = rounds;
            sceneManagerNode = sceneManagerContent;
            rssiNode = new RSSIAnalyserNode(eventManager);
            patreonsNode = new PatreonsNode();
            pilotPointsNode = new PointsSummaryNode(eventManager);
            pilotLapsListNode = new LapCountSummaryNode(eventManager);

            pbListNode = new LapRecordsSummaryNode(eventManager);
            pilotChanelList = new PilotChanelList(eventManager);

            replayNode = new ReplayNode(eventManager);

            AddTab("Rounds", this.rounds, ShowRounds);
            liveButton = AddTab("Live", sceneManagerNode, ShowLive);
            replayButton = AddTab("Replay", replayNode, ShowReplay);

            AddTab("Lap Records", pbListNode, ShowTopLaps);
            AddTab("Lap Count", pilotLapsListNode, ShowLaps);
            AddTab("Points", pilotPointsNode, ShowPoints);
            AddTab("Channel List", pilotChanelList, ShowPilotChannelList);
            rssiButton = AddTab("RSSI Analyser", rssiNode, ShowAnalyser);
            AddTab("Patreons", patreonsNode, ShowPatreons);

            replayButton.Enabled = false;

            eventManager.RaceManager.OnRaceChanged += UpdateReplayButton;
            eventManager.RaceManager.OnRaceEnd += UpdateReplayButton;
            eventManager.RaceManager.TimingSystemManager.OnInitialise += UpdateRSSIVisible;

            UpdateRSSIVisible();

            ShowPatreons();
        }


        public override void Dispose()
        {
            eventManager.RaceManager.OnRaceChanged -= UpdateReplayButton;
            eventManager.RaceManager.OnRaceEnd -= UpdateReplayButton;
            eventManager.RaceManager.TimingSystemManager.OnInitialise -= UpdateRSSIVisible;

            base.Dispose();
        }


        private void UpdateRSSIVisible()
        {
            rssiButton.Visible = eventManager.RaceManager.TimingSystemManager.HasSpectrumAnalyser;
        }

        private void UpdateReplayButton(Race race)
        {
            if (race != null && race.Ended)
            {
                replayButton.Enabled = VideoManager.HasReplay(race);
            }
            else
            {
                replayButton.Enabled = false;
            }
        }

        public override void Update(GameTime gameTime)
        {
            if (IsAnimating() && Showing == sceneManagerNode)
            {
                sceneManagerNode.Snap();
            }

            base.Update(gameTime);
        }

        public void ShowAnalyser(MouseInputEvent mie)
        {
            Show(rssiNode);

            if (!eventManager.RaceManager.RaceRunning)
            {
                var groups = eventManager.Channels.GetChannelGroups();
                var frequencies = groups.Select(c => c.FirstOrDefault());

                eventManager.RaceManager.TimingSystemManager.SetListeningFrequencies(frequencies.Select(r => new Timing.ListeningFrequency(r.Frequency, 1)));
                eventManager.RaceManager.TimingSystemManager.StartDetection();
            }
        }

        public void ShowPatreons(MouseInputEvent mie)
        {
            ShowPatreons();
        }

        public void ShowPatreons()
        {
            Show(patreonsNode);
            patreonsNode.Refresh();
        }

        public void ShowTopLaps(MouseInputEvent mie)
        {
            if (mie != null && mie.Button == MouseButtons.Middle)
            {
                BaseGame baseGame = CompositorLayer.Game as BaseGame;
                baseGame.QuickLaunchWindow<LapRecordsSummaryNode>(eventManager);
                return;
            }

            if (pbListNode.Visible)
            {
                pbListNode.Refresh();
            }
            Show(pbListNode);
            pbListNode.Scale(0.9f);
        }

        public void ShowPoints(MouseInputEvent mie)
        {
            if (mie != null && mie.Button == MouseButtons.Middle)
            {
                BaseGame baseGame = CompositorLayer.Game as BaseGame;
                baseGame.QuickLaunchWindow<PointsSummaryNode>(eventManager);
                return;
            }

            pilotPointsNode.OrderByLast();
            pilotPointsNode.Refresh();
            Show(pilotPointsNode);
        }

        public void ShowLaps(MouseInputEvent mie)
        {
            if (mie != null && mie.Button == MouseButtons.Middle)
            {
                BaseGame baseGame = CompositorLayer.Game as BaseGame;
                baseGame.QuickLaunchWindow<LapCountSummaryNode>(eventManager);
                return;
            }

            pilotLapsListNode.OrderByLast();
            pilotLapsListNode.Refresh();
            Show(pilotLapsListNode);
        }

        public void ShowRounds(MouseInputEvent mie)
        {
            eventManager.RoundManager.CheckThereIsOneRound();
            if (mie != null && mie.Button == MouseButtons.Middle)
            {
                BaseGame baseGame = CompositorLayer.Game as BaseGame;
                baseGame.QuickLaunchWindow<RoundsNode>(eventManager);
                return;
            }

            ShowRounds();
        }

        public void ShowRounds()
        {
            Race race = eventManager.RaceManager.CurrentRace;
            if (race != null)
            {
                rounds.ScrollToRace(race);
            }

            Show(rounds);
        }

        public void ShowPilotChannelList(MouseInputEvent mie)
        {
            if (mie != null && mie.Button == MouseButtons.Middle)
            {
                BaseGame baseGame = CompositorLayer.Game as BaseGame;
                baseGame.QuickLaunchWindow<PilotChanelList>(eventManager);
                return;
            }

            Show(pilotChanelList);
        }

        public void ShowLive(SceneManagerNode.Scenes scene)
        {
            sceneManagerNode.SetScene(scene);
            Show(sceneManagerNode);
        }

        private void ShowLive(MouseInputEvent mie)
        {
            if (mie != null && mie.Button == MouseButtons.Right)
            {
                MouseMenu sceneMenu = new MouseMenu(this);
                foreach (SceneManagerNode.Scenes scene in Enum.GetValues(typeof(SceneManagerNode.Scenes)))
                {
                    sceneMenu.AddItem(scene.ToString().CamelCaseToHuman(), () =>
                    {
                        ShowLive(scene);
                    });
                }

                sceneMenu.Show(liveButton.Bounds.X, liveButton.Bounds.Bottom);
            }
            else
            {
                ShowLive();
            }
        }

        public void ShowLive()
        {
            if (!eventManager.RaceManager.HasPilots)
            {
                sceneManagerNode.SetScene(SceneManagerNode.Scenes.PreRace, true);
            }
            else if (eventManager.RaceManager.RaceRunning)
            {
                sceneManagerNode.SetScene(SceneManagerNode.Scenes.Race, true);
            }
            else if (eventManager.RaceManager.RaceFinished)
            {
                sceneManagerNode.SetScene(SceneManagerNode.Scenes.PostRace, true);
            }
            else
            {
                sceneManagerNode.SetScene(SceneManagerNode.Scenes.PreRace, true);
            }

            if (eventManager.RaceManager.CurrentRace == null)
            {
                eventManager.RaceManager.LastFinishedRace();
            }

            Show(sceneManagerNode);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            id.PushClipRectangle(Bounds);
            base.Draw(id, parentAlpha);
            id.PopClipRectangle();
        }

        public void ShowReplay(MouseInputEvent mie)
        {
            Race current = eventManager.RaceManager.CurrentRace;
            Show(replayNode);
            replayNode.ReplayRace(current);
        }

        public override void Show(Node node)
        {
            base.Show(node);
            replayNode.CleanUp();
            GC.Collect();
        }
    }
}
