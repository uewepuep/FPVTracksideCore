using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Nodes
{
    public class GraphNode : Node
    {

        private List<GraphSeries> series;

        public GraphNode() 
        {
            series = new List<GraphSeries>();
        }

        public GraphSeries GetGraphSeries(string name, Color color)
        {
            GraphSeries got = series.FirstOrDefault(s => s.Name == name);
            if (got == null)
            {
                got = new GraphSeries(name, color);
                series.Add(got);    
            }
            return got;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);

            foreach (GraphSeries s in series)
            {
                if (!s.Points.Any())
                    continue;

                Vector2 last = s.Points.First();
                foreach (Vector2 point in s.Points.Skip(1))
                {
                    id.DrawLine(last, point, s.Color, s.Thickness);

                    last = point;
                }
            }
        }
    }

    public class GraphSeries
    {
        public Color Color { get; set; }

        public string Name { get; set; }

        public float Thickness { get; set; }

        public IEnumerable<Vector2> Points { get { return points; } }

        private List<Vector2> points;

        public GraphSeries(string name, Color color)
        {
            points = new List<Vector2>();
            Name = name;
            Color = color;
            Thickness = 2.5f;
        }

        public void Clear()
        {
            points.Clear(); 
        }

        public void AddPoint(float x, float y)
        {
            AddPoint(new Vector2(x, y));
        }

        public void AddPoint(Vector2 point)
        {
            points.Add(point);
        }
    }
}
