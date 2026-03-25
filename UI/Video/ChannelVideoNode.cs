using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition;
using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
using Tools;
using UI.Nodes;

namespace UI.Video
{
    public class ChannelVideoNode : ChannelNodeBase
    {
        public FrameNodeThumb FrameNode { get; private set; }
        private FrameSource source;
        private QRScanner qrScanner;

        public event Action<Channel, string> OnQRPilotDetected;

        public ChannelVideoNode(EventManager eventManager, Channel channel, FrameSource source, Color channelColor)
            : base(eventManager, channel, channelColor)
        {
            this.source = source;
        }

        protected override Node CreateDisplayNode()
        {
            FrameNode = new FrameNodeThumb(source);
            FrameNode.KeepAspectRatio = false;
            FrameNode.ThumbnailEnabled = ApplicationProfileSettings.Instance.VideoStaticDetector || EventManager.RaceManager.TimingSystemManager.HasVideoTiming;
            return FrameNode;
        }

        public override void Dispose()
        {
            qrScanner?.Dispose();
            qrScanner = null;
            base.Dispose();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (qrScanner != null && Pilot == null && !EventManager.RaceManager.RaceRunning && EventManager.RaceManager.CurrentRace != null)
            {
                qrScanner.Update(gameTime);
            }
        }

        public override void Init()
        {
            base.Init();

            if (ApplicationProfileSettings.Instance.QRPilotScan)
            {
                qrScanner = new QRScanner(FrameNode);
                qrScanner.OnTextDetected += (string name) => { OnQRPilotDetected?.Invoke(Channel, name); };
            }

            if (LapsNode != null)
            {
                LapsNode.RelativeBounds = new RectangleF(0, 1 - LapLineHeight, 1, LapLineHeight);
                LapsNode.BackgroundVisible = true;
                LapsNode.LapLines = 1;
            }

            if (SplitsNode != null)
            {
                SplitsNode.RelativeBounds = new RectangleF(0, LapsNode.RelativeBounds.Y - LapLineHeight, 1, LapLineHeight);
            }

            if (DisplayNode != null)
            {
                DisplayNode.RelativeBounds = new RectangleF(0, 0, 1, LapsNode.RelativeBounds.Y);
            }

            if (DisplayNode != null)
            {
                DisplayNode.RelativeBounds = new RectangleF(0, 0.25f, 1, 0.5f);
            }
        }

        public override void SetLapsVisible(bool visible)
        {
            base.SetLapsVisible(visible);

            if (LapsNode.Visible)
            {
                DisplayNode.RelativeBounds = new RectangleF(0, 0, 1, LapsNode.RelativeBounds.Y);
            }
            else
            {
                DisplayNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            }
        }
    }
}