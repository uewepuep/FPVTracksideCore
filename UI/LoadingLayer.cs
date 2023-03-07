using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;

namespace UI
{
    public class LoadingLayer : CompositorLayer
    {
        private TextNode LoadingText;

        private BorderPanelShadowNode borderPanelNode;
        private ProgressBarNode progressBar;

        public WorkQueue WorkQueue { get; set; }

        public bool BlockOnLoading { get; set; }
       
        public LoadingLayer(GraphicsDevice device)
            : base(device)
        {
            WorkQueue = new WorkQueue("Loading");
            WorkQueue.OnEnqueue += WorkQueue_OnEnqueue;
            WorkQueue.OnCompleteOne += OnCompleteOne;
            WorkQueue.OnCompleteLast += OnCompleteLast;
            WorkQueue.BeforeStart += WorkQueue_BeforeStart;

            borderPanelNode = new BorderPanelShadowNode();
            borderPanelNode.Scale(0.2f, 0.06f);
            borderPanelNode.Translate(0, 0.3f);
            Root.AddChild(borderPanelNode);

            LoadingText = new TextNode("Loading", Theme.Current.TextMain.XNA);
            LoadingText.RelativeBounds = new RectangleF(0.1f, 0.1f, 0.8f, 0.35f);
            LoadingText.Alignment = Tools.RectangleAlignment.Center;
            borderPanelNode.AddChild(LoadingText);

            progressBar = new AnimatedProgressBarNode(Theme.Current.TextMain.XNA, @"img/progresswave.png");
            progressBar.RelativeBounds = new RectangleF(0.025f, 0.55f, 0.95f, 0.3f);
            borderPanelNode.AddChild(progressBar);
          
            Visible = false;
            BlockOnLoading = true;
            RequestLayout();
        }

        private void WorkQueue_BeforeStart(WorkItem obj)
        {
            progressBar.Progress = WorkQueue.Progress;
            LoadingText.Text = obj.Name;

            // This keeps the text synced but also adds a lot of loading time? 
            //while (!LoadingText.IsUpToDate)
            //{
            //    Thread.Sleep(10);
            //}
        }

        private void OnCompleteLast()
        {
            Visible = false;

            progressBar.Progress = 0;
        }

        private void OnCompleteOne()
        {
            progressBar.Progress = WorkQueue.Progress;
        }

        private void WorkQueue_OnEnqueue()
        {
            Visible = true;
            progressBar.Progress = WorkQueue.Progress;
        }

        public override void Dispose()
        {
            WorkQueue.Dispose();
            base.Dispose();
        }

        protected override void OnDraw()
        {
            if (BlockOnLoading)
            {
                base.OnDraw();
            }
        }

        public override bool OnMouseInput(MouseInputEvent inputEvent)
        {
            if (BlockOnLoading)
            {
                return Visible;
            }
            else
            {
                return base.OnMouseInput(inputEvent);
            }
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (BlockOnLoading)
            {
                return Visible;
            }
            else
            {
                return base.OnKeyboardInput(inputEvent);
            }
        }
    }
}
