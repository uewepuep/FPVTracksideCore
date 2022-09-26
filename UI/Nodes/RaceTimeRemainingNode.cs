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
    public class RaceTimeRemainingNode : RaceTimeNode
    {
        public RaceTimeRemainingNode(RaceManager raceManager, Color textColor) 
            : base(raceManager, textColor)
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

            SetTime(RaceManager.RemainingTime);
        }
    }
}
