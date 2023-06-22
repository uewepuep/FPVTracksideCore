using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Nodes
{
    public class AutoRunnerTimerNode : TextNode
    {
        private AutoRunner autoRunner;

        public AutoRunnerTimerNode(AutoRunner autoRunner) 
            : base("", Theme.Current.TextMain.XNA)
        {
            this.autoRunner = autoRunner;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (!autoRunner.Paused && autoRunner.State != AutoRunner.States.None)
            {
                TimeSpan time = autoRunner.Timer;

                if (time == default)
                {
                    Text = "";
                }

                switch (autoRunner.State)
                {
                    case AutoRunner.States.WaitingRaceStart:
                        Text = "Next Race Start in " + time.TotalSeconds.ToString("0") + "s";
                        break;

                    case AutoRunner.States.WaitingResults:
                        Text = "Showing Results for " + time.TotalSeconds.ToString("0") + "s";
                        break;

                    case AutoRunner.States.WaitingRaceFinalLap:
                        Text = ""; // "Race Ends in " + time.TotalSeconds.ToString("0") + "s";
                        break;

                    case AutoRunner.States.WaitVideo:
                        Text = "Video Issue " + time.TotalSeconds.ToString("0") + "s";
                        break;
                }

                base.Draw(id, parentAlpha);
            }
        }
    }
}
