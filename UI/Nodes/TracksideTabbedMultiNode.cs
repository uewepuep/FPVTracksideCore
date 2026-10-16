using Composition;
using Composition.Input;
using Composition.Nodes;
using ExternalData;
using Microsoft.Xna.Framework;
using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Tools;
using UI.Nodes.Rounds;
using UI.Video;

namespace UI.Nodes
{
    public class TracksideTabbedMultiNode : TabbedMultiNode
    {
        public bool IsOnLive { get { return sceneManagerNode == Showing; } }
        public bool IsOnRounds { get { return rounds == Showing; } }
        public bool IsOnReplay { get { return ReplayNode == Showing; } }
        public bool IsOnChanelList { get { return pilotChanelList == Showing; } }
        public bool IsOnResults { get { return ResultsNode == Showing; } }
        public bool IsOnLapCount { get { return IsOnResults && ResultsNode.Showing == ResultsNode.LapCountSummaryNode; } }
        public bool IsOnLapRecords { get { return IsOnResults && ResultsNode.Showing == ResultsNode.LapRecordsSummaryNode; } }
        public bool IsOnPoints { get { return IsOnResults && ResultsNode.Showing == ResultsNode.PointsSummaryNode; } }
        public bool IsOnPatreons { get { return patreonsNode == Showing; } }
        public bool IsOnPhotoBooth { get { return PhotoBooth == Showing; } }
        public bool IsOnRSSI { get { return rssiNode == Showing; } }

        private RoundsNode rounds;
        private SceneManagerNode sceneManagerNode;
        private PilotChanelList pilotChanelList;
        private PatreonsNode patreonsNode;
        public PhotoBoothNode PhotoBooth { get; private set; }

        public ReplayNode ReplayNode { get; private set; }

        public ResultsNode ResultsNode { get; private set; }
        public TextButtonNode ResultsButton { get; private set; }

        private RSSIAnalyserNode rssiNode;

        private TextButtonNode replayButton;
        private TextButtonNode liveButton;

        protected EventManager eventManager;
        private VideoManager VideoManager;
        private KeyboardShortcuts keyMapper;

        private TextButtonNode rssiButton;
        public TextButtonNode PhotoBoothButton { get; private set; }


        public TracksideTabbedMultiNode(EventManager eventManager, VideoManager videoManager, SoundManager soundManager, RoundsNode rounds, SceneManagerNode sceneManagerContent, TabButtonsNode tabButtons, KeyboardShortcuts keyMapper)
            : base(TimeSpan.FromSeconds(0.6f), tabButtons)
        {
            this.eventManager = eventManager;
            this.keyMapper = keyMapper;
            VideoManager = videoManager;

            this.rounds = rounds;
            sceneManagerNode = sceneManagerContent;
            rssiNode = new RSSIAnalyserNode(eventManager);
            patreonsNode = new PatreonsNode();
            ResultsNode = new ResultsNode(eventManager);
            // Switching sub-tab counts as a tab change (OBS triggers, pilot list).
            ResultsNode.OnTabChange += (text, node) => { OnTabChange?.Invoke(text, node); };
            pilotChanelList = new PilotChanelList(eventManager);
            PhotoBooth = new PhotoBoothNode(videoManager, eventManager, soundManager);

            ReplayNode = new ReplayNode(eventManager, keyMapper);

            eventManager.RaceManager.OnRaceChanged += UpdateReplayButton;
            eventManager.RaceManager.OnRaceEnd += UpdateReplayButton;
            eventManager.RaceManager.TimingSystemManager.OnInitialise += UpdateRSSIVisible;
            videoManager.OnFinishedFinalizing += VideoManager_OnFinishedFinalizing;
            eventManager.OnJumpToReplay += ShowReplay;
            eventManager.OnJumpToReplaySecondWindow += ShowReplaySecondWindow;
        }

        public override void Dispose()
        {
            eventManager.RaceManager.OnRaceChanged -= UpdateReplayButton;
            eventManager.RaceManager.OnRaceEnd -= UpdateReplayButton;
            eventManager.RaceManager.TimingSystemManager.OnInitialise -= UpdateRSSIVisible;
            eventManager.OnJumpToReplay -= ShowReplay;
            eventManager.OnJumpToReplaySecondWindow -= ShowReplaySecondWindow;

            base.Dispose();
        }

        private void VideoManager_OnFinishedFinalizing()
        {
            Race race = eventManager.RaceManager.CurrentRace;
            if (race != null)
            {
                UpdateReplayButton(race);   
            }
        }

        public virtual void Init(PlatformTools platformTools)
        {
            AddTab("Rounds", rounds, ShowRounds);
            liveButton = AddTab("Live", sceneManagerNode, ShowLive);
            replayButton = AddTab("Replay", ReplayNode, (mie) => { ShowReplay(); });

            ResultsButton = AddTab("Results", ResultsNode, ShowResults);
            AddTab("Channel List", pilotChanelList, ShowPilotChannelList);
            rssiButton = AddTab("RSSI Analyser", rssiNode, ShowAnalyser);
            PhotoBoothButton = AddTab("Photo Booth", PhotoBooth, ShowPhotoBooth);
            AddTab("Patreons", patreonsNode, ShowPatreons);

            replayButton.Enabled = false;
            PhotoBoothButton.Visible = platformTools.HasFeature(PlatformFeature.Video);

            UpdateRSSIVisible();

            ShowPatreons();
        }


        private void UpdateRSSIVisible()
        {
            rssiButton.Visible = eventManager.RaceManager.TimingSystemManager.HasSpectrumAnalyser;
        }

        private void UpdateReplayButton(Race race)
        {
            if (race != null && race.Ended)
            {
                replayButton.Enabled = VideoManager.HasReplay(race) && !VideoManager.Finalising;
            }
            else
            {
                replayButton.Enabled = false;
            }

            if (race == null)
            {
                if (Showing == ReplayNode)
                {
                    ShowLive();
                }
            }
        }

        public void ShowAnalyser(MouseInputEvent mie)
        {
            Show(rssiNode);

            if (!eventManager.RaceManager.RaceRunning)
            {
                var groups = eventManager.Channels.GetChannelGroups();
                var frequencies = groups.Select(c => c.FirstOrDefault());

                Timing.ListeningFrequency[] lastFrequencies = eventManager.RaceManager.TimingSystemManager.LastListeningFrequencies;
                if (lastFrequencies.Length == 0)
                {
                    lastFrequencies = frequencies.Select(r => new Timing.ListeningFrequency(r.Band.ToString(), r.Number, r.Frequency, 1, Color.Red)).ToArray();
                }

                eventManager.RaceManager.TimingSystemManager.SetListeningFrequencies(lastFrequencies);

                DateTime now = DateTime.Now;
                eventManager.RaceManager.TimingSystemManager.StartDetection(ref now);
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

        public void ShowResults(MouseInputEvent mie)
        {
            ShowResults();
        }

        public void ShowResults()
        {
            // Results first, so a sub-tab change is passed on with Results showing.
            Show(ResultsNode);
            ResultsNode.Show();
        }

        public void ShowRounds(MouseInputEvent mie)
        {
            eventManager.RoundManager.CheckThereIsOneRound();
            //if (mie != null && mie.Button == MouseButtons.Middle)
            //{
            //    BaseGame baseGame = CompositorLayer.Game as BaseGame;
            //    baseGame.QuickLaunchWindow<RoundsNode>(eventManager, keyMapper);
            //    return;
            //}

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
        public void ShowPhotoBooth(MouseInputEvent mie)
        {
            Show(PhotoBooth);
            PhotoBooth.Load();
        }

        public void ShowPilotChannelList(MouseInputEvent mie)
        {
            //if (mie != null && mie.Button == MouseButtons.Middle)
            //{
            //    BaseGame baseGame = CompositorLayer.Game as BaseGame;
            //    baseGame.QuickLaunchWindow<PilotChanelList>(eventManager, keyMapper);
            //    return;
            //}

            Show(pilotChanelList);
        }

        public void ShowCommentators()
        {
            Show(sceneManagerNode);
            sceneManagerNode.ShowCommentators();
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
                sceneManagerNode.SetScene(SceneManagerNode.Scenes.RaceResults, true);
            }
            else
            {
                sceneManagerNode.SetScene(SceneManagerNode.Scenes.PreRace, true);
            }

            if (eventManager.RaceManager.CurrentRace == null)
            {
                Race race = eventManager.RaceManager.LastFinishedRace();
                if (race != null) 
                { 
                    eventManager.RaceManager.SetRace(race);
                }
            }

            Show(sceneManagerNode);
        }

        public void ShowReplay(Race race, Lap lap = null)
        {
            if (!eventManager.RaceManager.RaceRunning)
            {
                if (eventManager.RaceManager.CurrentRace != race)
                {
                    eventManager.RaceManager.SetRace(race);
                }

                Show(ReplayNode);
                ReplayNode.ReplayRace(race, lap);
            }
        }

        public void ShowReplay()
        {
            Race current = eventManager.RaceManager.CurrentRace;
            ShowReplay(current);
        }

        private void ShowReplaySecondWindow(Race race, Lap lap = null)
        {
            BaseGame baseGame = CompositorLayer.Game as BaseGame;
            if (baseGame == null)
                return;

            var replayNode = new ReplayNode(eventManager, keyMapper);
            replayNode.ReplayRace(race, lap);
            baseGame.ShowNewWindow(replayNode);
        }

        public override void Show(Node node)
        {
            if (Showing == rssiNode)
            {
                TimingSystemManager tsm = eventManager.RaceManager.TimingSystemManager;

                // if we're detecting and not in a race
                if (!eventManager.RaceManager.RaceRunning && tsm.IsDetecting)
                {
                    tsm.EndDetection(EndDetectionType.Abort);
                }
            }

            if (Showing == PhotoBooth)
            {
                PhotoBooth.Clean();
                eventManager.ProfilePictures.FindProfilePictures(eventManager.Event.Pilots.ToArray());
                sceneManagerNode.ChannelsGridNode.ReloadPilotProfileImages();
            }

            base.Show(node);
            ReplayNode.CleanUp();
            GC.Collect();
        }
    }
}
