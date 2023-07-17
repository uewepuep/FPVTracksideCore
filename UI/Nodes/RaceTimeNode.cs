using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Video;

namespace UI.Nodes
{
    public class RaceTimeNode : TextNode, IUpdateableNode
    {
        public RaceManager RaceManager { get; private set; }
        public ReplayNode ReplayNode { get; private set; }
        public string Prepend { get; set; }

        public RaceTimeNode(RaceManager raceManager, ReplayNode replayNode, Color textColor) 
            : base("0.00", textColor)
        {
            ReplayNode = replayNode;
            RaceManager = raceManager;
            Prepend = "Time ";
            Alignment = RectangleAlignment.CenterRight;
        }

        public virtual void Update(GameTime gameTime)
        {
            if (ReplayNode != null && ReplayNode.Active)
            {
                SetTime(ReplayNode.ElapsedTime);
            }
            else
            {
                SetTime(RaceManager.ElapsedTime);
            }
        }

        public void SetTime(TimeSpan timespan)
        {
            if (timespan < TimeSpan.Zero)
                timespan = TimeSpan.Zero;

            Text = Prepend + timespan.ToStringRaceTime(1);
        }
    }
}
