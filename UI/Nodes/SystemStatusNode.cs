using Composition;
using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Sound;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Tools;
using UI.Video;

namespace UI.Nodes
{
    public class SystemStatusNode : Node
    {
        public TimingSystemManager TimingSystemManager { get; private set; }
        public VideoManager VideoManager { get; private set; }
        public SoundManager SoundManager { get; private set; }

        private MuteStatusNode tts;

        public SystemStatusNode(TimingSystemManager timingSystemManager, VideoManager videoManager, SoundManager soundManager)
        {
            TimingSystemManager = timingSystemManager;
            VideoManager = videoManager;
            SoundManager = soundManager;

            SetupStatuses();
        }

        public void SetupStatuses()
        {
            ClearDisposeChildren();

            tts = new MuteStatusNode(SoundManager, true);

            AddChild(tts);
            AddChild(new MuteStatusNode(SoundManager, false));

            foreach (ITimingSystem timingSystem in TimingSystemManager.TimingSystems)
            {
                TimingSystemStatusNode tsn = new TimingSystemStatusNode(TimingSystemManager, timingSystem);
                AddChild(tsn);
            }

            foreach (VideoConfig videoConfig in VideoManager.VideoConfigs)
            {
                VideoSystemStatusNode vsn = new VideoSystemStatusNode(VideoManager, videoConfig);
                AddChild(vsn);
            }
            RequestLayout();
        }

        protected override void LayoutChildren(Rectangle bounds)
        {
            int left = bounds.Left;
            int ItemPadding = 10;
            int ItemHeight = 30;
            int width = bounds.Width;

            tts.Visible = SoundManager.HasSpeech();

            Node[] nodes = VisibleChildren.ToArray();

            int prevBottom = bounds.Y;
            foreach (Node n in nodes)
            {
                n.RelativeBounds = new RectangleF(0, 0, 1, 1);
                n.Layout(new Rectangle(left,
                                       prevBottom + ItemPadding,
                                       width,
                                       ItemHeight));

                prevBottom = n.Bounds.Bottom;
            }
        }
    }

    public class StatusNode : Node, IUpdateableNode
    {
        protected TextNode status;
        protected TextNode name;
        protected ImageNode icon;

        private DateTime recvTimeout;
        private bool statusOK;

        private Color tint;
        public Color Tint
        {
            set
            {
                status.Tint = value;
                name.Tint = value;
                icon.Tint = value;
                tint = value;
            }
            get
            {
                return tint;
            }
        }

        public string Name
        {
            get
            {
                return name.Text;
            }
            set
            {
                name.Text = value;
                if (name.Text.Length > 4)
                {
                    name.Text = name.Text.Substring(0, 4);
                }
            }
        }

        private DateTime lastStatusUpdate;

        protected float updateEverySeconds;

        public StatusNode(string iconFilename)
        {
            updateEverySeconds = 2;

            tint = Color.White;
            icon = new ImageNode(iconFilename);
            icon.Alignment = RectangleAlignment.CenterLeft;
            icon.RelativeBounds = new RectangleF(0, 0, 0.5f, 1f);
            icon.Scale(0.7f);
            AddChild(icon);

            float textLeft = 0.4f;

            name = new TextNode("", Color.White);
            name.RelativeBounds = new RectangleF(textLeft, 0.0f, 1 - textLeft, 0.5f);
            name.Alignment = RectangleAlignment.CenterRight;
            name.OverrideHeight = 14;
            AddChild(name);

            status = new TextNode("", Color.White);
            status.RelativeBounds = new RectangleF(textLeft, name.RelativeBounds.Bottom, 1 - textLeft, 0.5f);
            status.Alignment = RectangleAlignment.CenterRight;
            status.OverrideHeight = name.OverrideHeight;
            AddChild(status);
        }

        public void OnDataRecv()
        {
            recvTimeout = DateTime.Now.AddMilliseconds(100);
        }

        public void SetStatus(string status, bool statusOK)
        {
            this.statusOK = statusOK;
            this.status.Text = status;
        }

        public void Update(GameTime gameTime)
        {
            if ((DateTime.Now - lastStatusUpdate).TotalSeconds > updateEverySeconds)
            {
                StatusUpdate();
                lastStatusUpdate = DateTime.Now;
            }

            Color color = statusOK ? Color.White : Color.Red;

            if (recvTimeout >= DateTime.Now)
            {
                color = Color.Orange;
            }

            status.Tint = color;
            icon.Tint = color;
            name.Tint = color;
        }

        public virtual void StatusUpdate()
        {
        }

    }

    public class TimingSystemStatusNode : StatusNode
    {
        public TimingSystemManager TimingSystemManager { get; private set; }
        public ITimingSystem TimingSystem { get; private set; }

        public TimingSystemStatusNode(TimingSystemManager timingSystemManager, ITimingSystem timingSystem)
            : base(@"img/timing.png")
        {
            TimingSystemManager = timingSystemManager;
            TimingSystem = timingSystem;
            TimingSystemManager.OnDataReceived += TimingSystemManager_OnDataReceived;

            Name = TimingSystemToAcronym(timingSystem.Type);
        }

        private void TimingSystemManager_OnDataReceived(ITimingSystem obj)
        {
            if (obj == TimingSystem)
            {
                OnDataRecv();
            }
        }

        private string TimingSystemToAcronym(TimingSystemType type)
        {
            switch (type)
            {
                case TimingSystemType.Delta5:
                    return "D5";

                case TimingSystemType.Dummy:
                    return "DMY";

                case TimingSystemType.LapRF:
                    return "LRF";

                case TimingSystemType.Video:
                    return "V";

                case TimingSystemType.Chorus:
                    return "CHR";
                default:
                    return Maths.AutoAcronym(type.ToString());
            }

        }

        public override void StatusUpdate()
        {
            base.StatusUpdate();

            StatusItem[] statuses = TimingSystem.Status.ToArray();

            if (statuses == null || !statuses.Any())
                return;

            IEnumerable<StatusItem> alarmed = statuses.Where(s => !s.StatusOK);
            if (alarmed.Any())
            {
                StatusItem chosen = alarmed.GetFromCurrentTime(updateEverySeconds);
                SetStatus(chosen.Value, false);
            }
            else 
            {
                StatusItem chosen = statuses.GetFromCurrentTime(updateEverySeconds);
                SetStatus(chosen.Value, TimingSystem.Connected);
            }
        }
    }

    public class VideoSystemStatusNode : StatusNode
    {
        public VideoManager VideoManager { get; private set; }
        public VideoConfig VideoConfig { get; private set; }

        private TextNode recordingIcon;

        public VideoSystemStatusNode(VideoManager videoManager, VideoConfig videoConfig)
            : base(@"img/video.png")
        {
            recordingIcon = new TextNode("●", Color.Red);
            recordingIcon.RelativeBounds = new RectangleF(0.0f, 0.3f, 0.8f, 0.8f);
            icon.AddChild(recordingIcon);

            VideoManager = videoManager;
            VideoConfig = videoConfig;

            Name = VideoConfig.DeviceName.AutoAcronym();
        }

        public override void StatusUpdate()
        {
            base.StatusUpdate();
            bool connected, recording;
            int height;
            if (VideoManager.GetStatus(VideoConfig, out connected, out recording, out height))
            {
                SetStatus(height + "p", connected);
                recordingIcon.Visible = recording;
            }
            else
            {
                recordingIcon.Visible = false;
                SetStatus("", false);
            }

        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                MouseMenu mm = new MouseMenu(this);

                foreach (var source in VideoManager.FrameSources)
                {
                    if (source.VideoConfig == VideoConfig)
                    {
                        mm.AddItem("Repair " + source.VideoConfig.DeviceName, () =>
                        {
                            VideoManager.Initialize(source);
                        });
                    }
                }

                mm.Show(this);
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }

    public class MuteStatusNode : StatusNode
    {
        private SoundManager soundManager;

        private CheckboxNode cbn;

        private bool tts;

        public bool Value
        {
            get
            {
                if (tts)
                {
                    return soundManager.MuteTTS;
                }
                else
                {
                    return soundManager.MuteWAV;
                }
            }
            set
            {
                if (tts)
                {
                    soundManager.MuteTTS = value;
                }
                else
                {
                    soundManager.MuteWAV = value;
                }
            }
        }

        public MuteStatusNode(SoundManager soundManager, bool tts)
            : base("")
        {
            this.soundManager = soundManager;
            this.tts = tts;

            if (tts)
            {
                name.Text = "TTS";
            }
            else
            {
                name.Text = "WAV";
            }

            cbn = new CheckboxNode();
            cbn.TickFilename = @"img/unmute.png";
            cbn.UnTickFilename = @"img/mute.png";
            cbn.Alignment = icon.Alignment;
            cbn.RelativeBounds = icon.RelativeBounds;
            cbn.ValueChanged += Cbn_ValueChanged;
            AddChild(cbn);
            icon.Dispose();
            icon = cbn;

            SetMute(Value);
        }

        private void SetMute(bool mute)
        {
            SetStatus(mute ? "Mute" : "", !mute);
            Value = mute;
            cbn.Value = !mute;
        }

        private void Cbn_ValueChanged(bool obj)
        {
            SetMute(!obj);
        }
    }
}
