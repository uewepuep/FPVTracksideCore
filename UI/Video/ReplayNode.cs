using Composition;
using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Media;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;

namespace UI.Video
{
    public class ReplayNode : Node, IUpdateableNode
    {
        public ChannelsGridNode ChannelsGridNode { get; private set; }

        public EventManager EventManager { get; private set; }
        public VideoManager PlaybackVideoManager { get; private set; }

        public SeekNode SeekNode { get; private set; }

        private const float seekBarHeight = 0.05f;

        private IPlaybackFrameSource primary;
        private Race race;

        private DateTime minStart;
        private DateTime maxEnd;

        public bool Active
        {
            get
            {
                return race != null;
            }
        }

        public TimeSpan ElapsedTime 
        {
            get
            {
                if (race == null || SeekNode == null) 
                    return TimeSpan.Zero;

                return SeekNode.CurrentTime - race.Start;
            }
        }

        public TimeSpan RemainingTime
        {
            get
            {
                if (race == null || SeekNode == null)
                    return TimeSpan.Zero;

                return race.End - SeekNode.CurrentTime;
            }
        }

        private KeyboardShortcuts keyMapper;

        public ReplayNode(EventManager eventManager, KeyboardShortcuts keyMapper)
        {
            EventManager = eventManager;
            this.keyMapper = keyMapper;

            EventManager.RaceManager.OnLapDetected += OnChange;
            EventManager.RaceManager.OnLapsRecalculated += OnChange;
            EventManager.RaceManager.OnLapDisqualified += OnChange;


            SeekNode = new SeekNode(eventManager, Theme.Current.Replay.Text.XNA);
            SeekNode.RelativeBounds = new RectangleF(0.1f, 1 - seekBarHeight, 0.8f, seekBarHeight);
            SeekNode.Seek += Seek;
            SeekNode.PlayButton.OnClick += (m) => { Play(); };
            SeekNode.StopButton.OnClick += (m) => { Stop(); };
            SeekNode.SlowCheck.Checkbox.ValueChanged += Checkbox_ValueChanged;
            SeekNode.ShowAll.OnClick += ShowAll_OnClick;

            AddChild(SeekNode);
        }

        private void OnChange(Race race)
        {
            OnChange();
        }

        private void OnChange(Lap lap)
        {
            OnChange();
        }

        private void OnChange()
        {
            if (primary != null && race != null)
            {
                SeekNode.SetRace(race, minStart, maxEnd);
                ChannelsGridNode.SetPlaybackTime(race.Start);
            }
        }

        private void Checkbox_ValueChanged(bool obj)
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.SlowMotion = obj;
            }
        }

        public override void Dispose()
        {
            EventManager.RaceManager.OnLapDetected -= OnChange;
            EventManager.RaceManager.OnLapsRecalculated -= OnChange;
            EventManager.RaceManager.OnLapDisqualified -= OnChange;

            base.Dispose();
            CleanUp();
        }

        private void Stop()
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.Pause();
            }

            SeekNode.PlayButton.Visible = true;
            SeekNode.StopButton.Visible = false;

            RequestLayout();
        }

        private void Play()
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.Play();
            }

            SeekNode.PlayButton.Visible = false;
            SeekNode.StopButton.Visible = true;

            RequestLayout();
        }

        public void Seek(DateTime seekTime)
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.SetPosition(seekTime);
            }
        }

        public void PrevFrame()
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.PrevFrame();
            }
        }

        public void NextFrame()
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.NextFrame();
            }
        }

        public void Step(int steps)
        {
            float frameRate = (float)Math.Max(1, Math.Min(120, primary.FrameRate));
            double step = 1.0 / frameRate;

            TimeSpan seekTime = primary.MediaTime + TimeSpan.FromSeconds(step * steps);
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.SetPosition(seekTime);
            }
        }

        private IEnumerable<IPlaybackFrameSource> GetFileFrameSources()
        {
            if (PlaybackVideoManager != null)
            {
                return PlaybackVideoManager.GetFrameSources().OfType<IPlaybackFrameSource>();
            }
            return new IPlaybackFrameSource[0];
        }

        public void CleanUp()
        {
            if (ChannelsGridNode != null)
            {
                ChannelsGridNode.Dispose();
                ChannelsGridNode = null;
            }

            if (PlaybackVideoManager != null)
            {
                PlaybackVideoManager.Dispose();
                PlaybackVideoManager = null;
            }
            primary = null;
            race = null;
        }

        public bool ReplayRace(Race race)
        {
            try
            {
                CleanUp();
                SeekNode.ClearFlags();

                this.race = race;

                PlaybackVideoManager = VideoManagerFactory.CreateVideoManager();
                PlaybackVideoManager.OnStart += PlaybackVideoManager_OnStart;

                ChannelsGridNode = new ChannelsGridNode(EventManager, PlaybackVideoManager);
                ChannelsGridNode.RelativeBounds = new RectangleF(0, 0, 1, 1 - seekBarHeight);
                AddChild(ChannelsGridNode);

                PlaybackVideoManager.LoadRecordings(race, (fs) =>
                {
                    IEnumerable<IPlaybackFrameSource> frameSources = fs.OfType<IPlaybackFrameSource>();

                    if (frameSources.Any())
                    {
                        minStart = frameSources.Select(r => r.StartTime).Min();
                        maxEnd = frameSources.Select(r => r.StartTime + r.Length).Max();
                    }
                    else
                    {
                        minStart = DateTime.MaxValue;
                        maxEnd = DateTime.MinValue;
                    }

                    foreach (IPlaybackFrameSource fileFrameSource in frameSources)
                    {
                        fileFrameSource.Mute();
                    }

                    // show everything in video playback.
                    foreach (FrameSource frameSource in frameSources)
                    {
                        foreach(VideoBounds videoBounds in frameSource.VideoConfig.VideoBounds)
                        {
                            if (videoBounds.SourceType == SourceTypes.Commentators 
                             || videoBounds.SourceType == SourceTypes.Launch 
                             || videoBounds.SourceType == SourceTypes.FinishLine)
                            {
                                videoBounds.ShowInGrid = true;
                            }
                        }
                    }

                    ChannelsGridNode.FillChannelNodes();
                    ChannelsGridNode.MakeExtrasVisible(true);

                    foreach (PilotChannel pilotChannel in race.PilotChannelsSafe)
                    {
                        ChannelNodeBase cbn = ChannelsGridNode.AddPilot(pilotChannel);
                        cbn.OnCloseClick += () => { Hide(cbn); };
                        cbn.OnCrashedOutClick += () => { Hide(cbn); };
                    }


                    SeekNode.SetRace(race, minStart, maxEnd);

                    RequestLayout();
                });

                SeekNode.PlayButton.Visible = false;
                SeekNode.StopButton.Visible = true;
                SeekNode.SlowCheck.Visible = true;
                SeekNode.ShowAll.Visible = false;
                SeekNode.SlowCheck.Checkbox.Value = false;


                return true;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                CleanUp();

                return false;
            }
        }

        private void PlaybackVideoManager_OnStart(FrameSource obj)
        {
            if (primary == null)
            {
                primary = (IPlaybackFrameSource)obj;
            }
            PlaybackVideoManager.OnStart -= PlaybackVideoManager_OnStart;
        }

        private void Hide(ChannelNodeBase cbn)
        {
            cbn.Visible = false;
            SeekNode.ShowAll.Visible = true;
            ChannelsGridNode.Reorder(true);
        }

        private void ShowAll_OnClick(MouseInputEvent mie)
        {
            SeekNode.ShowAll.Visible = false;

            foreach (ChannelNodeBase cbn in ChannelsGridNode.ChannelNodes)
            {
                if (cbn.Pilot != null)
                {
                    cbn.Visible = true;
                    cbn.SetCrashedOutType(ChannelNodeBase.CrashOutType.None);
                }
            }
            ChannelsGridNode.Reorder();
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (inputEvent.ButtonState == ButtonStates.Pressed)
            {
                if (keyMapper.ReplayPlayStop.Match(inputEvent))
                {
                    if (SeekNode.PlayButton.Visible)
                    {
                        Play();
                    }
                    else
                    {
                        Stop();
                    }
                }

                if (SeekNode.PlayButton.Visible)
                {
                    if (keyMapper.ReplayPrevFrame.Match(inputEvent))
                    {
                        PrevFrame();
                    }
                    if (keyMapper.ReplayNextFrame.Match(inputEvent))
                    {
                        NextFrame();
                    }
                }

                if (keyMapper.ReplayPlus5Seconds.Match(inputEvent))
                {
                    Seek(primary.CurrentTime + TimeSpan.FromSeconds(5));
                }

                if (keyMapper.ReplayMinus5Seconds.Match(inputEvent))
                {
                    Seek(primary.CurrentTime + TimeSpan.FromSeconds(-5));
                }
            }
            return base.OnKeyboardInput(inputEvent);
        }

        public void Update(GameTime gameTime)
        {
            if (PlaybackVideoManager != null && ChannelsGridNode != null)
            {
                HasFocus = true;

                if (primary != null)
                {
                    DateTime currentTime = primary.CurrentTime;

                    if (Math.Abs((SeekNode.CurrentTime - currentTime).TotalMilliseconds) > 10)
                    {
                        SeekNode.CurrentTime = currentTime;
                        SeekNode.RequestLayout();

                        ChannelsGridNode.SetPlaybackTime(currentTime);
                        ChannelsGridNode.SetReorderType(ApplicationProfileSettings.Instance.PilotOrderMidRace == ApplicationProfileSettings.OrderTypes.Channel ? ChannelsGridNode.ReOrderTypes.ChannelOrder : ChannelsGridNode.ReOrderTypes.PositionOrder);
                        ChannelsGridNode.Reorder();
                    }
                }
            }
        }


    }
}
