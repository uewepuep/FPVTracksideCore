using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class AutoRunnerControls : ColorNode
    {
        private TextButtonNode lessTimeNode;
        private TextButtonNode moreTimeNode;
        private IconButtonNode controlButton;
        private TextNode timeRemaining;

        private ImageNode pauseNode;

        public AutoRunner AutoRunner { get; private set; }

        public bool LargeMode { get { return bottomButtonsContainer.Visible; } }

        private Node bottomButtonsContainer;

        public AutoRunnerControls(AutoRunner autoRunner)
            : base(Theme.Current.RightControls.Foreground)
        {
            AutoRunner = autoRunner;

            controlButton = new IconButtonNode(@"img\auto.png", "Auto", Theme.Current.RightControls.Foreground, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
            controlButton.OnClick += ControlButton_OnClick;
            AddChild(controlButton);

            float pauseSize = 0.25f;
            pauseNode = new ImageNode(@"img\pause.png");
            pauseNode.Visible = false;
            pauseNode.RelativeBounds = new RectangleF(1 - pauseSize, 0, pauseSize, pauseSize);
            controlButton.AddChild(pauseNode);

            bottomButtonsContainer = new Node();
            bottomButtonsContainer.RelativeBounds = new RectangleF(0.0f, controlButton.RelativeBounds.Bottom, 1, 1 - controlButton.RelativeBounds.Bottom);
            AddChild(bottomButtonsContainer);


            timeRemaining = new TextNode("", Theme.Current.RightControls.Text.XNA);
            timeRemaining.RelativeBounds = new RectangleF(0.0f, 0, 1, 0.4f);
            bottomButtonsContainer.AddChild(timeRemaining);

            float bottomButtonHeight = timeRemaining.RelativeBounds.Bottom;

            lessTimeNode = new TextButtonNode("-", Theme.Current.RightControls.Foreground, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
            lessTimeNode.RelativeBounds = new RectangleF(0, bottomButtonHeight, 0.5f, 1 - bottomButtonHeight);
            lessTimeNode.OnClick += LessTimeNode_OnClick;
            bottomButtonsContainer.AddChild(lessTimeNode);

            moreTimeNode = new TextButtonNode("+", Theme.Current.RightControls.Foreground, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
            moreTimeNode.RelativeBounds = new RectangleF(0.5f, bottomButtonHeight, 0.5f, 1 - bottomButtonHeight);
            moreTimeNode.OnClick += MoreTimeNode_OnClick;
            bottomButtonsContainer.AddChild(moreTimeNode);

            bottomButtonsContainer.Visible = false;
        }

        private void ControlButton_OnClick(Composition.Input.MouseInputEvent mie)
        {
            AutoRunner.TogglePause();
        }

        private void MoreTimeNode_OnClick(Composition.Input.MouseInputEvent mie)
        {
            AutoRunner.Timer += TimeSpan.FromSeconds(10);
        }

        private void LessTimeNode_OnClick(Composition.Input.MouseInputEvent mie)
        {
            AutoRunner.Timer -= TimeSpan.FromSeconds(10);
        }

        public override void Layout(RectangleF parentBounds)
        {
            if (LargeMode) 
            {
                controlButton.RelativeBounds = new RectangleF(0.0f, 0, 1, 0.61f);
                bottomButtonsContainer.RelativeBounds = new RectangleF(0.0f, controlButton.RelativeBounds.Bottom, 1, 1 - controlButton.RelativeBounds.Bottom);
            }
            else
            {
                controlButton.RelativeBounds = new RectangleF(0.0f, 0, 1, 1);
            }

            base.Layout(parentBounds);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            pauseNode.Visible = AutoRunner.Paused;

            bottomButtonsContainer.Visible = AutoRunner.State != AutoRunner.States.None;

            string time = " " + AutoRunner.Timer.TotalSeconds.ToString("0") + "s";

            switch (AutoRunner.State)
            {
                case AutoRunner.States.None:

                    if (AutoRunner.Paused)
                    {
                        controlButton.Text = "Paused";
                    }
                    else
                    {
                        controlButton.Text = "Idle";
                    }
                    timeRemaining.Text = "";
                    break;
                    
                case AutoRunner.States.WaitingRaceStart:
                    controlButton.Text = "Start";
                    timeRemaining.Text = time;
                    break;

                case AutoRunner.States.WaitingRaceFinalLap:
                    controlButton.Text = "Final Lap";
                    timeRemaining.Text = time;
                    break;

                case AutoRunner.States.WaitingResults:
                    controlButton.Text = "Results";
                    timeRemaining.Text = time;
                    break;

                case AutoRunner.States.WaitVideo:
                    controlButton.Text = "Video issue";
                    timeRemaining.Text = time;
                    break;
            }

            base.Draw(id, parentAlpha);
        }
    }
}
