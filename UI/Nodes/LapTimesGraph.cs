using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Nodes
{
    public class LapTimesGraph : GraphNode
    {
        public EventManager EventManager { get; private set; }

        public LapTimesGraph(EventManager eventManager) 
        { 
            EventManager = eventManager;
        }

        public void SetRace(Race race)
        {
            Clear();

            float minLapTime = float.MaxValue;
            float maxLapTime = float.MinValue;
            float lapCount = 0;

            foreach (PilotChannel pilotChannel in race.PilotChannelsSafe)
            {
                Color color = EventManager.GetChannelColor(pilotChannel.Channel);

                GraphSeries series = GetCreateSeries(pilotChannel.PilotName, color);
                foreach (Lap lap in race.GetValidLaps(pilotChannel.Pilot, true))
                {
                    float lapTime = (float)lap.Length.TotalSeconds;

                    minLapTime = Math.Min(minLapTime, lapTime);
                    maxLapTime = Math.Max(maxLapTime, lapTime);
                    lapCount = Math.Max(lap.Number, lapCount);

                    series.AddPoint(lap.Number, lapTime);
                }
            }

            View = new RectangleF(-1, maxLapTime, lapCount + 2, minLapTime- maxLapTime);

            for (int i = 0; i <= lapCount; i++)
            {
                AddXLabel(i, "L" + i.ToString());
            }

            for (float i = minLapTime; i <= maxLapTime; i += 1)
            {
                AddYLabel(i, i.ToString("0") + "s");
            }
        }
    }
}
