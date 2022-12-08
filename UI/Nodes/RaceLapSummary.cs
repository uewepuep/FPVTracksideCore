using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class RaceLapSummary : AspectNode
    {
        public RaceLapSummary() 
        {
            AspectRatio = 2f;
        }

        public void SetRace(EventManager em, Race currentRace)
        {
            ClearDisposeChildren();

            if (em.RaceManager.RaceFinished)
            {
                IEnumerable<Result> results = em.ResultManager.GetResults(currentRace).OrderBy(r => r.Position);

                foreach (Result result in results)
                {
                    Pilot pilot = result.Pilot;
                    Color color = em.GetChannelColor(currentRace.GetChannel(pilot));

                    Node container = new PanelNode();
                    AddChild(container);

                    TextNode pilotName = new TextNode(pilot.Name, Theme.Current.TextMain.XNA);
                    pilotName.RelativeBounds = new RectangleF(0, 0.2f, 1 / 5f, 0.6f);
                    container.AddChild(pilotName);


                    LapsNode lapsNode = new LapsNode(em);
                    lapsNode.ChannelColor = color;
                    lapsNode.SetPilot(pilot);
                    lapsNode.RelativeBounds = new RectangleF(pilotName.RelativeBounds.Right, 0, 1 - pilotName.RelativeBounds.Right, 1);
                    container.AddChild(lapsNode);

                    pilotName.Scale(0.8f, 1);
                }

                AlignVertically(0.05f, Children);
            }
        }
    }
}
