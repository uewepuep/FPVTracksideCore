using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class GraphNode : Node
    {
        private List<GraphSeries> series;

        private Dictionary<float, string> xLabels;
        private Dictionary<float, string> yLabels;

        public RectangleF View { get; set; }

        public GraphNode() 
        {
            series = new List<GraphSeries>();
            xLabels = new Dictionary<float, string>();
            yLabels = new Dictionary<float, string>();
        }

        public void Clear()
        {
            lock (series)
            {
                series.Clear();
            }

            lock (xLabels)
            {
                xLabels.Clear();
            }

            lock (yLabels)
            {
                yLabels.Clear();    
            }
        }

        public GraphSeries GetCreateSeries(string name, Color color)
        {
            lock (series)
            {
                GraphSeries got = series.FirstOrDefault(s => s.Name == name);
                if (got == null)
                {
                    got = new GraphSeries(this, name, color);
                    series.Add(got);
                }
                return got;
            }
        }

        public Vector2 ToPixel(Vector2 v)
        {
            Vector2 v2 = new Vector2(v.X - View.X, v.Y - View.Y);
            v2.X = (v2.X / View.Width) * Bounds.Width;
            v2.X += Bounds.X;

            v2.Y = (v2.Y / View.Height) * Bounds.Height;
            v2.Y += Bounds.Y;

            return v2;
        }

        private void DrawGrid(Drawer id)
        {
            Color color = Color.Gray;

            lock (xLabels)
            {
                foreach (var kvp in xLabels)
                {
                    float px = ToPixel(new Vector2(kvp.Key)).X;
                    id.DrawLine(new Vector2(px, Bounds.Top), new Vector2(px, Bounds.Bottom), color);
                }
            }

            lock (yLabels)
            {
                foreach (var kvp in yLabels)
                {
                    float py = ToPixel(new Vector2(kvp.Key)).Y;
                    id.DrawLine(new Vector2(Bounds.Left, py), new Vector2(Bounds.Right, py), color);
                }
            }
        }

        public void AddXLabel(float value, string label)
        {
            lock (xLabels)
            {
                xLabels.Add(value, label);
            }
        }

        public void AddYLabel(float value, string label)
        {
            lock (yLabels)
            {
                yLabels.Add(value, label);
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);

            DrawGrid(id);

            lock (series)
            {
                foreach (GraphSeries s in series)
                {
                    s.Draw(id);
                }
            }
        }
    }

    public class GraphSeries
    {
        public Color Color { get; private set; }

        public string Name { get; set; }

        public float Thickness { get; set; }

        public IEnumerable<Vector2> Points { get { return points; } }

        private List<Vector2> points;

        private Texture2D texture;

        private GraphNode graph;

        public GraphSeries(GraphNode graph, string name, Color color)
        {
            this.graph = graph;
            points = new List<Vector2>();
            Name = name;
            Color = color;
            Thickness = 2.5f;
        }

        public void Clear()
        {
            lock (points)
            {
                points.Clear();
            }
        }

        public void AddPoint(float x, float y)
        {
            AddPoint(new Vector2(x, y));
        }

        public void AddPoint(Vector2 point)
        {
            lock (points)
            {
                points.Add(point);
            }
        }

        public void Draw(Drawer id)
        {
            if (texture == null)
            {
                texture = id.TextureCache.GetTextureFromColor(Color);
            }

            if (!Points.Any())
                return;

            int ithickness = (int)Math.Ceiling(Thickness) * 2;
            int idoubleThickness = (int)Math.Ceiling(Thickness * 4);
            lock (points)
            {
                Vector2 last = graph.ToPixel(Points.First());
                foreach (Vector2 point in Points.Skip(1))
                {
                    Vector2 p = graph.ToPixel(point);

                    id.DrawLine(last, p, texture, Thickness);
                    last = p;

                    id.Draw(texture, new Rectangle((int)p.X - ithickness, (int)p.Y - ithickness, idoubleThickness, idoubleThickness), Color.White, 1);
                }
            }
        }
    }
}
