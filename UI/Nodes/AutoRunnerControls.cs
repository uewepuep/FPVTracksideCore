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
        private IconButtonNode controlButton;

        private ImageNode pauseNode;

        public AutoRunner AutoRunner { get; private set; }

        public AutoRunnerControls(AutoRunner autoRunner)
            : base(Theme.Current.RightControls.Foreground)
        {
            AutoRunner = autoRunner;

            controlButton = new IconButtonNode(@"img\auto.png", "", Theme.Current.RightControls.Foreground, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
            controlButton.OnClick += ControlButton_OnClick;
            AddChild(controlButton);

            float badgeSize = 0.25f;

            pauseNode = new ImageNode(@"img\pause.png");
            pauseNode.Tint = Theme.Current.RightControls.Text.XNA;
            pauseNode.Visible = false;
            pauseNode.RelativeBounds = new RectangleF(1 - badgeSize, 0, badgeSize, badgeSize);
            controlButton.AddChild(pauseNode);
        }

        private void ControlButton_OnClick(Composition.Input.MouseInputEvent mie)
        {
            AutoRunner.TogglePause();
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            pauseNode.Visible = AutoRunner.Paused;

            AutoRunner.States state = AutoRunner.State;
            if (!AutoRunner.ShowText)
            {
                state = AutoRunner.States.None;
            }

            int secondsRemaining = (int)Math.Ceiling(AutoRunner.Timer.TotalSeconds);
            string time = secondsRemaining > 0 ? " - " + secondsRemaining + "s" : "";

            switch (state)
            {
                case AutoRunner.States.None:

                    if (AutoRunner.Paused)
                    {
                        controlButton.Text = Translator.Get("AutoRunner.Paused", "Paused");
                    }
                    else
                    {
                        controlButton.Text = Translator.Get("AutoRunner.Auto", "Auto");
                    }
                    break;

                case AutoRunner.States.WaitingRaceStart:
                    controlButton.Text = Translator.Get("AutoRunner.Start", "Start") + time;
                    break;

                case AutoRunner.States.WaitingRaceFinalLap:
                    controlButton.Text = Translator.Get("AutoRunner.FinalLap", "Final Lap") + time;
                    break;

                case AutoRunner.States.WaitingResults:
                    controlButton.Text = Translator.Get("AutoRunner.Results", "Results") + time;
                    break;

                case AutoRunner.States.WaitVideo:
                    controlButton.Text = Translator.Get("AutoRunner.Video", "Video issue") + time;
                    break;
            }

            base.Draw(id, parentAlpha);
        }
    }
}
