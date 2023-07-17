using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Video;

namespace UI.Nodes
{
    public class RaceTimeRemainingNode : RaceTimeNode
    {
        public RaceTimeRemainingNode(RaceManager raceManager, ReplayNode replayNode, Color textColor) 
            : base(raceManager, replayNode, textColor)
        {
            Prepend = "Remaining ";
        }

        public override void Update(GameTime gameTime)
        {
            if (RaceManager.EventManager.Event != null)
            {
                if (RaceManager.EventManager.Event.RaceLength == TimeSpan.Zero)
                {
                    Text = "";
                    return;
                }
            }

            if (RaceManager.EventType == EventTypes.CasualPractice)
            {
                Text = "";
                return;
            }

            if (ReplayNode != null && ReplayNode.Active)
            {
                SetTime(ReplayNode.RemainingTime);
            }
            else
            {
                SetTime(RaceManager.RemainingTime);
            }
        }
    }
}
