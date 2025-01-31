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

        public RectangleF View { get; set; }

        public GraphNode() 
        {
            series = new List<GraphSeries>();
        }

        public void Clear()
        {
            lock (series)
            {
                series.Clear();
            }
        }

        public GraphSeries GetCreateSeries(string name, Color color)
        {
            lock (series)
            {
                GraphSeries got = series.FirstOrDefault(s => s.Name == name);
                if (got == null)
                {
                    got = new GraphSeries(name, color);
                    series.Add(got);
                }
                return got;
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);
            lock (series)
            {
                foreach (GraphSeries s in series)
                {
                    s.Draw(id, View, Bounds);
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

        public GraphSeries(string name, Color color)
        {
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

        private static Vector2 ToPixel(Vector2 v, RectangleF view, Rectangle bounds)
        {
            Vector2 v2 = new Vector2(v.X - view.X, v.Y - view.Y);
            v2.X = (v2.X / view.Width) * bounds.Width;
            v2.X += bounds.X;

            v2.Y = (v2.Y / view.Height) * bounds.Height;
            v2.Y += bounds.Y;

            return v2;
        }

        public void Draw(Drawer id, RectangleF view, Rectangle bounds)
        {
            if (texture == null)
            {
                texture = id.TextureCache.GetTextureFromColor(Color);
            }

            if (!Points.Any())
                return;

            int ithickness = (int)Math.Ceiling(Thickness);
            int idoubleThickness = (int)Math.Ceiling(Thickness * 2);
            lock (points)
            {
                Vector2 last = ToPixel(Points.First(), view, bounds);
                foreach (Vector2 point in Points.Skip(1))
                {
                    Vector2 p = ToPixel(point, view, bounds);

                    id.DrawLine(last, p, texture, Thickness);
                    last = p;

                    id.Draw(texture, new Rectangle((int)p.X - ithickness, (int)p.Y - ithickness, idoubleThickness, idoubleThickness), Color.White, 1);
                }
            }
        }
    }
}
