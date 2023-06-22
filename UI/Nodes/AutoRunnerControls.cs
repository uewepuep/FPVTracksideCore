using Composition;
using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class AutoRunnerControls : Node
    {
        private TextButtonNode lessTimeNode;
        private TextButtonNode moreTimeNode;
        private IconButtonNode controlButton;

        private ImageNode pauseNode;

        public AutoRunner AutoRunner { get; private set; }

        public AutoRunnerControls(AutoRunner autoRunner) 
        {
            AutoRunner = autoRunner;

            float b = 0.3f;
            float oneminusb = 1 - b;


            controlButton = new IconButtonNode(@"img\auto.png", "Auto", Theme.Current.RightControls.Foreground, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
            controlButton.RelativeBounds = new RectangleF(0.0f, 0, 1, oneminusb);
            controlButton.OnClick += ControlButton_OnClick;
            AddChild(controlButton);

            pauseNode = new ImageNode(@"img\pause.png");
            pauseNode.Visible = false;
            controlButton.AddChild(pauseNode);

            lessTimeNode = new TextButtonNode("-", Theme.Current.RightControls.Foreground, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
            lessTimeNode.RelativeBounds = new RectangleF(0, oneminusb, 0.5f, b);
            lessTimeNode.OnClick += LessTimeNode_OnClick;
            AddChild(lessTimeNode);

            moreTimeNode = new TextButtonNode("+", Theme.Current.RightControls.Foreground, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
            moreTimeNode.RelativeBounds = new RectangleF(0.5f, oneminusb, 0.5f, b);
            moreTimeNode.OnClick += MoreTimeNode_OnClick;
            AddChild(moreTimeNode);
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

        public override void Draw(Drawer id, float parentAlpha)
        {
            pauseNode.Visible = AutoRunner.Paused;

            if (AutoRunner.Config.AutoRunRaces && AutoRunner.State != AutoRunner.States.None)
            {
                string time = " (" + AutoRunner.Timer.TotalSeconds.ToString("0") + ")";

                switch (AutoRunner.State)
                {
                    case AutoRunner.States.None:
                        controlButton.Text = "Auto";
                        break;
                    case AutoRunner.States.WaitingRaceEnd:
                        controlButton.Text = "End" + time;
                        break;
                    case AutoRunner.States.WaitingRaceStart:
                        controlButton.Text = "Start" + time;
                        break;
                    case AutoRunner.States.WaitingResults:
                        controlButton.Text = "Results" + time;
                        break;
                }

                base.Draw(id, parentAlpha);
            }
        }
    }
}
